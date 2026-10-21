"""
Builds the tiny stand-ins for NLLB-200 that the unit tests run through the real ONNX code path.

The real model is 1.2 GB, so the tests use:
  * a 64-piece SentencePiece BPE model trained on a few sentences (same format as NLLB's), and
  * "echo" encoder/decoder graphs with NLLB's input/output names. The decoder copies the source
    back one token per step, but only if the caller keeps the key/value cache and the
    use_cache_branch flag right; otherwise its scores are all zero and the output is garbage.
Both decoder layouts are produced: one merged decoder, and decoder + decoder_with_past.

Run from this folder with: pip install onnx numpy sentencepiece onnxruntime, then python make_fixtures.py
"""
import json
import os

import numpy as np
import onnx
import sentencepiece as spm
from onnx import TensorProto as T
from onnx import helper as h
from onnx import numpy_helper

HERE = os.path.dirname(os.path.abspath(__file__))
LAYERS, HEADS, HEAD_SIZE = 2, 2, 4
LANGUAGE_INDEX = {"eng_Latn": 46, "tgl_Latn": 173, "deu_Latn": 41}
CORPUS = [
    "Hello, how are you today?", "Thank you very much.", "Good morning, my friend.",
    "I love you.", "The cat and the dog are friends.", "Where is the water?",
    "Kumusta ka? Salamat po.", "Magandang umaga, kaibigan.", "Mahal kita.", "Nasaan ang tubig?",
    "Guten Morgen, mein Freund.", "Danke sch?n.", "Ich liebe dich.", "Wo ist das Wasser?",
    "This is a small test of the translator.", "We are learning languages together.",
] * 20

def train_tokenizer():
    """One copy at the root; the test project links it into merged/ and split/ when it builds."""
    corpus = os.path.join(HERE, "corpus.txt")
    with open(corpus, "w", encoding="utf-8") as f:
        f.write("\n".join(CORPUS))
    prefix = os.path.join(HERE, "sentencepiece.bpe")
    spm.SentencePieceTrainer.train(
        input=corpus, model_prefix=prefix, vocab_size=64, model_type="bpe", character_coverage=1.0, minloglevel=2)
    os.remove(corpus)
    os.remove(prefix + ".vocab")
    return spm.SentencePieceProcessor(model_file=prefix + ".model")

def const(name, value, dtype=np.int64):
    return h.make_node("Constant", [], [name], value=numpy_helper.from_array(np.array(value, dtype=dtype), name + "_v"))

def save(graph, path):
    model = h.make_model(graph, opset_imports=[h.make_opsetid("", 17)], ir_version=8)
    onnx.checker.check_model(model)
    onnx.save(model, path)

def encoder(vocab, path):
    nodes = [
        const("depth", [vocab]), const("onoff", [0.0, 1.0], np.float32), const("last", [-1]),
        h.make_node("OneHot", ["input_ids", "depth", "onoff"], ["onehot"], axis=-1),
        h.make_node("Cast", ["attention_mask"], ["maskf"], to=T.FLOAT),
        h.make_node("Unsqueeze", ["maskf", "last"], ["mask3"]),
        h.make_node("Mul", ["onehot", "mask3"], ["last_hidden_state"]),
    ]
    graph = h.make_graph(nodes, "encoder",
        [h.make_tensor_value_info("input_ids", T.INT64, [1, "n"]), h.make_tensor_value_info("attention_mask", T.INT64, [1, "n"])],
        [h.make_tensor_value_info("last_hidden_state", T.FLOAT, [1, "n", vocab])])
    save(graph, path)

def decoder(vocab, kind, path):
    """kind: merged (one decoder, use_cache_branch), first (no cache in), past (cache in, decoder cache out)."""
    has_past = kind in ("merged", "past")
    inputs = [
        h.make_tensor_value_info("input_ids", T.INT64, [1, "k"]),
        h.make_tensor_value_info("encoder_attention_mask", T.INT64, [1, "n"]),
        h.make_tensor_value_info("encoder_hidden_states", T.FLOAT, [1, "n", vocab]),
    ]
    if has_past:
        for layer in range(LAYERS):
            for part, length in (("decoder", "past_k"), ("encoder", "past_n")):
                for kv in ("key", "value"):
                    inputs.append(h.make_tensor_value_info(f"past_key_values.{layer}.{part}.{kv}", T.FLOAT, [1, HEADS, length, HEAD_SIZE]))
    if kind == "merged":
        inputs.append(h.make_tensor_value_info("use_cache_branch", T.BOOL, [1]))

    nodes = [const("one", 1), const("two", 2), const("zero", 0), const("axis0", [0]), const("heads", [1, HEADS]), const("size", [HEAD_SIZE])]
    nodes += [
        h.make_node("Shape", ["input_ids"], ["ids_shape"]), h.make_node("Gather", ["ids_shape", "one"], ["k"]),
        h.make_node("Shape", ["encoder_hidden_states"], ["hs_shape"]), h.make_node("Gather", ["hs_shape", "one"], ["n"]),
    ]
    if has_past:
        nodes += [
            h.make_node("Shape", ["past_key_values.0.decoder.key"], ["pd_shape"]), h.make_node("Gather", ["pd_shape", "two"], ["past_len"]),
            h.make_node("Shape", ["past_key_values.0.encoder.key"], ["pe_shape"]), h.make_node("Gather", ["pe_shape", "two"], ["enc_len"]),
        ]
    else:
        nodes += [h.make_node("Identity", ["zero"], ["past_len"]), h.make_node("Identity", ["zero"], ["enc_len"])]

    # Echo: the scores at decoder position p are the one-hot of source token p.
    nodes += [
        h.make_node("Add", ["past_len", "k"], ["end"]), h.make_node("Range", ["past_len", "end", "one"], ["positions"]),
        h.make_node("Sub", ["n", "one"], ["n_1"]), h.make_node("Min", ["positions", "n_1"], ["clamped"]),
        h.make_node("Squeeze", ["encoder_hidden_states", "axis0"], ["hidden2d"]),
        h.make_node("Gather", ["hidden2d", "clamped"], ["rows"], axis=0),
        h.make_node("Unsqueeze", ["rows", "axis0"], ["raw"]),
        h.make_node("ReduceMin", ["encoder_attention_mask"], ["mask_min"], keepdims=0),
        h.make_node("Cast", ["mask_min"], ["mask_ok"], to=T.FLOAT),
        h.make_node("Mul", ["raw", "mask_ok"], ["masked"]),
    ]
    # Scores are zeroed unless the encoder cache is full-length on cached steps (and empty on the first).
    if kind == "merged":
        nodes += [
            h.make_node("Equal", ["enc_len", "n"], ["enc_full"]), h.make_node("Equal", ["enc_len", "zero"], ["enc_empty"]),
            h.make_node("And", ["use_cache_branch", "enc_full"], ["ok_cached"]),
            h.make_node("Not", ["use_cache_branch"], ["first_step"]), h.make_node("And", ["first_step", "enc_empty"], ["ok_first"]),
            h.make_node("Or", ["ok_cached", "ok_first"], ["ok"]), h.make_node("Cast", ["ok"], ["okf"], to=T.FLOAT),
        ]
    elif kind == "past":
        nodes += [h.make_node("Equal", ["enc_len", "n"], ["ok"]), h.make_node("Cast", ["ok"], ["okf"], to=T.FLOAT)]
    else:
        nodes += [const("okf", 1.0, np.float32)]
    nodes += [h.make_node("Mul", ["masked", "okf"], ["logits"])]

    nodes += [
        h.make_node("Unsqueeze", ["k", "axis0"], ["k1"]), h.make_node("Concat", ["heads", "k1", "size"], ["dec_shape"], axis=0),
        h.make_node("ConstantOfShape", ["dec_shape"], ["dec_new"]),
        h.make_node("Sub", ["n", "enc_len"], ["enc_missing"]), h.make_node("Unsqueeze", ["enc_missing", "axis0"], ["e1"]),
        h.make_node("Concat", ["heads", "e1", "size"], ["enc_shape"], axis=0), h.make_node("ConstantOfShape", ["enc_shape"], ["enc_new"]),
    ]
    outputs = [h.make_tensor_value_info("logits", T.FLOAT, [1, "k", vocab])]
    for layer in range(LAYERS):
        for kv in ("key", "value"):
            name = f"present.{layer}.decoder.{kv}"
            if has_past:
                nodes.append(h.make_node("Concat", [f"past_key_values.{layer}.decoder.{kv}", "dec_new"], [name], axis=2))
            else:
                nodes.append(h.make_node("Identity", ["dec_new"], [name]))
            outputs.append(h.make_tensor_value_info(name, T.FLOAT, [1, HEADS, "total_k", HEAD_SIZE]))
            if kind == "past":
                continue
            name = f"present.{layer}.encoder.{kv}"
            if has_past:
                nodes.append(h.make_node("Concat", [f"past_key_values.{layer}.encoder.{kv}", "enc_new"], [name], axis=2))
            else:
                nodes.append(h.make_node("Identity", ["enc_new"], [name]))
            outputs.append(h.make_tensor_value_info(name, T.FLOAT, [1, HEADS, "n", HEAD_SIZE]))
    save(h.make_graph(nodes, "decoder_" + kind, inputs, outputs), path)

def main():
    merged, split = os.path.join(HERE, "merged"), os.path.join(HERE, "split")
    os.makedirs(merged, exist_ok=True)
    os.makedirs(split, exist_ok=True)
    sp = train_tokenizer()
    pieces = sp.get_piece_size()
    vocab = pieces + 1 + 202 + 1  # fairseq offset, 202 language tags, <mask>

    encoder(vocab, os.path.join(merged, "encoder_model_quantized.onnx"))
    decoder(vocab, "merged", os.path.join(merged, "decoder_model_merged_quantized.onnx"))
    encoder(vocab, os.path.join(split, "encoder_model.onnx"))
    decoder(vocab, "first", os.path.join(split, "decoder_model.onnx"))
    decoder(vocab, "past", os.path.join(split, "decoder_with_past_model.onnx"))

    # What Hugging Face's NllbTokenizer would produce: [tag] (piece + 1, unknown = 3)... [</s>].
    cases = []
    for text, tag in (("Hello, how are you today?", "eng_Latn"), ("Salamat po, kaibigan.", "tgl_Latn"), ("Danke sch\u00f6n \u4f60!", "deu_Latn")):
        ids = [pieces + 1 + LANGUAGE_INDEX[tag]] + [3 if i == 0 else i + 1 for i in sp.encode(text)] + [2]
        cases.append({"text": text, "tag": tag, "ids": ids, "decoded": sp.decode(sp.encode(text))})
    with open(os.path.join(HERE, "expected.json"), "w", encoding="utf-8") as f:
        json.dump({"pieces": pieces, "cases": cases}, f, ensure_ascii=False, indent=2)
        f.write("\n")

if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Export GigaAM multilingual_ctc to sherpa-onnx NeMo CTC layout."""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path

import gigaam
import onnx
from onnxruntime.quantization import QuantType, quantize_dynamic


MODEL_NAME = "multilingual_ctc"
PREFIX = "gigaam_multilingual_ctc"


def add_meta_data(filename: Path, meta_data: dict[str, str]) -> None:
    model = onnx.load(str(filename))
    while len(model.metadata_props):
        model.metadata_props.pop()

    for key, value in meta_data.items():
        meta = model.metadata_props.add()
        meta.key = key
        meta.value = str(value)

    onnx.save(model, str(filename))


def resolve_labels(model) -> list[str]:
    cfg = getattr(model, "cfg", None)
    if cfg is not None:
        # GigaAM multilingual: cfg.decoding.vocabulary
        decoding = cfg.get("decoding") if hasattr(cfg, "get") else None
        if decoding is None and hasattr(cfg, "decoding"):
            decoding = cfg.decoding
        if decoding is not None:
            vocab = decoding.get("vocabulary") if hasattr(decoding, "get") else getattr(decoding, "vocabulary", None)
            if vocab is not None:
                return list(vocab)

        if hasattr(cfg, "get") and "labels" in cfg:
            return list(cfg["labels"])
        if hasattr(cfg, "labels"):
            return list(cfg.labels)

    for attr in ("labels", "vocab", "tokenizer"):
        value = getattr(model, attr, None)
        if value is None:
            continue
        if isinstance(value, (list, tuple)):
            return list(value)
        if isinstance(value, dict):
            return [value[i] for i in sorted(value.keys())]

    raise RuntimeError("Could not resolve CTC labels/vocab from model")


def write_tokens(path: Path, labels: list[str], blank_id: int | None = None) -> int:
    # Sherpa NeMo CTC expects blank as the last token id.
    if blank_id is None:
        blank_id = len(labels)
    with path.open("w", encoding="utf-8", newline="\n") as f:
        for i, token in enumerate(labels):
            f.write(f"{token} {i}\n")
        f.write(f"<blk> {blank_id}\n")
    return blank_id + 1


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--out-dir",
        type=Path,
        default=Path("artifacts") / "gigaam-multilingual-ctc",
    )
    parser.add_argument(
        "--download-root",
        type=Path,
        default=Path("artifacts") / "gigaam-download",
    )
    parser.add_argument("--skip-int8", action="store_true")
    args = parser.parse_args()

    out_dir: Path = args.out_dir
    out_dir.mkdir(parents=True, exist_ok=True)
    args.download_root.mkdir(parents=True, exist_ok=True)

    print(f"Loading {MODEL_NAME} …")
    model = gigaam.load_model(
        MODEL_NAME,
        fp16_encoder=False,
        use_flash=False,
        download_root=str(args.download_root),
    )

    labels = resolve_labels(model)
    blank_id = None
    decoding = getattr(model, "decoding", None)
    if decoding is not None and hasattr(decoding, "blank_id"):
        blank_id = int(decoding.blank_id)
    tokens_path = out_dir / f"{PREFIX}_tokens.txt"
    vocab_size = write_tokens(tokens_path, labels, blank_id=blank_id)
    print(f"Wrote {tokens_path} (vocab_size={vocab_size})")

    work_dir = out_dir / "_export_work"
    if work_dir.exists():
        shutil.rmtree(work_dir)
    work_dir.mkdir(parents=True, exist_ok=True)

    print("Exporting ONNX …")
    # Newer GigaAM accepts dtype=; older builds may only take dir_path.
    try:
        model.to_onnx(dir_path=str(work_dir), dtype=None)
    except TypeError:
        model.to_onnx(dir_path=str(work_dir))

    # GigaAM writes "<name>.onnx" into dir_path.
    candidates = sorted(work_dir.glob("*.onnx"))
    if not candidates:
        raise FileNotFoundError(f"No ONNX produced in {work_dir}")

    # Prefer the CTC encoder model file matching model name.
    src = next((p for p in candidates if MODEL_NAME in p.name), candidates[0])
    fp32_path = out_dir / f"{PREFIX}.onnx"
    shutil.copy2(src, fp32_path)

    meta_data = {
        "vocab_size": str(vocab_size),
        "normalize_type": "",
        "subsampling_factor": "4",
        "model_type": "EncDecCTCModel",
        "version": "1",
        "model_author": "https://github.com/salute-developers/GigaAM",
        "license": "MIT",
        "language": "multilingual",
        "is_giga_am": "1",
        "model_name": "GigaAM multilingual_ctc",
        "url": "https://huggingface.co/ai-sage/GigaAM-Multilingual",
    }
    add_meta_data(fp32_path, meta_data)
    print(f"Wrote {fp32_path}")

    if not args.skip_int8:
        int8_path = out_dir / f"{PREFIX}_int8.onnx"
        print(f"Quantizing → {int8_path}")
        quantize_dynamic(
            model_input=str(fp32_path),
            model_output=str(int8_path),
            weight_type=QuantType.QUInt8,
        )
        print(f"Wrote {int8_path}")

    shutil.rmtree(work_dir, ignore_errors=True)
    print("Done.")


if __name__ == "__main__":
    main()

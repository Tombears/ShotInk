"""Run the real OCR protocol against fixed screenshots (Python standard library).

python tests/ocr-benchmark.py --host dist/ocr/ShotInk.OcrHost.exe
python tests/ocr-benchmark.py --dotnet dotnet --host ocr-host/bin/Release/net8.0/ShotInk.OcrHost.dll --matrix
"""
import argparse
import base64
import json
import pathlib
import struct
import subprocess
import sys
import time

# Windows CI may default redirected output to cp1252; OCR reports contain Chinese.
sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--host", required=True)
parser.add_argument("--dotnet")
parser.add_argument("--matrix", action="store_true")
parser.add_argument("--output", default="build/ocr-benchmark.json")
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parent.parent
host = str(pathlib.Path(args.host).resolve())
command = ([args.dotnet, host] if args.dotnet else [host])
configurations = [[], ["--quality"], ["--fast"], ["--fast", "--quality"]] if args.matrix else [[]]
report = []
for options in configurations:
    started = time.perf_counter()
    process = subprocess.Popen(command + options, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
    try:
        handshake = json.loads(process.stdout.readline())
        assert handshake.get("ready"), handshake
        entry = {"options": options, "engine": handshake["engine"], "startupMs": (time.perf_counter() - started) * 1000, "samples": []}
        request_id = 0
        for name in ("ocr-mixed.png", "ocr-table.png"):
            png = (root / "tests" / "fixtures" / name).read_bytes()
            width, height = struct.unpack(">II", png[16:24])
            for run in range(3):
                request_id += 1
                started = time.perf_counter()
                process.stdin.write(json.dumps({"id": request_id, "png": base64.b64encode(png).decode("ascii")}) + "\n")
                process.stdin.flush()
                document = json.loads(process.stdout.readline())
                elapsed = (time.perf_counter() - started) * 1000
                assert document.get("id") == request_id and not document.get("error"), document
                assert (document["width"], document["height"]) == (width, height)
                assert document["lines"], "Fixture produced no text"
                characters = 0
                for line in document["lines"]:
                    assert line["characters"], "Missing character coordinates"
                    # Host offsets use UTF-16, like the .NET Framework UI.
                    text = line["text"].encode("utf-16-le")
                    for char in line["characters"]:
                        x, y, w, h = char["box"]
                        assert 0 <= x < width and 0 <= y < height and w > 0 and h > 0
                        assert x + w <= width and y + h <= height
                        start, length = char["start"], char["length"]
                        assert text[start * 2:(start + length) * 2].decode("utf-16-le") == char["text"]
                        characters += 1
                entry["samples"].append({"image": name, "run": run, "engineMs": document["elapsedMs"], "roundtripMs": elapsed,
                    "characters": characters, "lines": [line["text"] for line in document["lines"]]})
        # Malformed images must yield a response and leave the worker usable.
        process.stdin.write('{"id":999,"png":"invalid"}\n')
        process.stdin.flush()
        failure = json.loads(process.stdout.readline())
        assert failure.get("id") == 999 and failure.get("error"), failure
        process.stdin.write(json.dumps({"id": 1000, "png": base64.b64encode(png).decode("ascii")}) + "\n")
        process.stdin.flush()
        recovery = json.loads(process.stdout.readline())
        assert recovery.get("id") == 1000 and recovery.get("lines"), recovery
        report.append(entry)
        print(json.dumps(entry, ensure_ascii=False), flush=True)
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
output = pathlib.Path(args.output)
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print("PASS: real OCR protocol, character coordinates and error recovery")

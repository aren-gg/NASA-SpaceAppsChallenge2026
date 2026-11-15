import pathlib
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib

SCRIPT = pathlib.Path(__file__).resolve().parents[1] / "make_heightmap.py"


class HeightmapTests(unittest.TestCase):
    def run_tool(self, *args):
        return subprocess.run(
            [sys.executable, str(SCRIPT), *map(str, args)], capture_output=True, text=True
        )

    def samples(self, path):
        data = path.read_bytes()
        self.assertEqual(data[:8], b"\x89PNG\r\n\x1a\n")
        self.assertEqual(struct.unpack(">IIBBBBB", data[16:29]), (256, 256, 16, 0, 0, 0, 0))
        offset = 8
        payload = b""
        while offset < len(data):
            count = struct.unpack(">I", data[offset:offset + 4])[0]
            kind = data[offset + 4:offset + 8]
            if kind == b"IDAT":
                payload += data[offset + 8:offset + 8 + count]
            offset += count + 12
        rows = zlib.decompress(payload)
        values = []
        for row in range(256):
            start = row * 513
            self.assertEqual(rows[start], 0)
            values.extend(struct.unpack(">256H", rows[start + 1:start + 513]))
        return values

    def test_fallback_repeatable_and_seeded(self):
        with tempfile.TemporaryDirectory() as directory:
            a = pathlib.Path(directory) / "a.png"
            b = pathlib.Path(directory) / "b.png"
            self.assertEqual(self.run_tool("--output", a, "--seed", 7).returncode, 0)
            self.assertEqual(self.run_tool("--output", b, "--seed", 7).returncode, 0)
            self.assertEqual(a.read_bytes(), b.read_bytes())
            self.assertGreater(len(set(self.samples(a))), 256)
            self.assertEqual(self.run_tool("--output", b, "--seed", 8).returncode, 0)
            self.assertNotEqual(a.read_bytes(), b.read_bytes())

    def test_mola_crop_uses_signed_big_endian(self):
        with tempfile.TemporaryDirectory() as directory:
            source = pathlib.Path(directory) / "mola.img"
            output = pathlib.Path(directory) / "mars.png"
            source.write_bytes(struct.pack(">16h", *range(-8, 8)))
            result = self.run_tool(
                "--source", source, "--source-width", 4, "--source-height", 4,
                "--crop", 1, 1, 2, 2, "--output", output
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            values = self.samples(output)
            self.assertEqual(values[0], 0)
            self.assertEqual(values[-1], 65535)

    def test_missing_source_warns_and_falls_back(self):
        with tempfile.TemporaryDirectory() as directory:
            output = pathlib.Path(directory) / "mars.png"
            result = self.run_tool(
                "--source", pathlib.Path(directory) / "missing.img", "--output", output
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("warning", result.stderr.lower())
            self.samples(output)


if __name__ == "__main__":
    unittest.main()

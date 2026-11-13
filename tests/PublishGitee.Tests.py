import contextlib
import hashlib
import importlib.util
import io
import json
import pathlib
import sys
import tempfile
import unittest
import urllib.parse
import zipfile

sys.dont_write_bytecode = True
ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('publisher', ROOT / 'scripts/publish-gitee-update.py')
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class PublishingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='ShotInk-publisher-')
        self.root = pathlib.Path(self.temporary.name)
        (self.root / 'src').mkdir()
        (self.root / 'releases').mkdir()
        (self.root / 'updates').mkdir()
        (self.root / 'src/AssemblyInfo.cs').write_text('[assembly: AssemblyInformationalVersion("1.13.0")]', encoding='utf-8')
        (self.root / 'releases/v1.13.0.md').write_text('Release notes', encoding='utf-8')
        self.full = self.package('ocr', 'ShotInk-portable.zip')
        self.lite = self.package('lite', 'ShotInk-portable-lite.zip')
        publisher.ROOT = self.root
        publisher.MAX_ATTACHMENT_SIZE = self.full.stat().st_size - 1
        publisher.saved_token = lambda: 'test-token'
        publisher.request = self.request
        publisher.form = lambda *args, **kwargs: {'id': 1}
        publisher.upload = self.upload
        self.assets = {}
        self.failure = False
        self.corrupt = False

    def tearDown(self):
        self.temporary.cleanup()

    def package(self, flavor, name):
        path = self.root / name
        with zipfile.ZipFile(path, 'w') as archive:
            for required in ['ShotInk.exe', 'update-portable.ps1', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll']:
                archive.writestr(required, b'fixture')
            archive.writestr('ShotInk.flavor', flavor)
            archive.writestr('ShotInk.build.sha256', 'A' * 64)
            if flavor == 'ocr':
                archive.writestr('ocr/ShotInk.OcrHost.exe', bytes(range(256)) * 128)
        return path

    def request(self, path, token='', method='GET', **kwargs):
        if path.startswith('/releases/tags/'):
            return {'id': 1, 'tag_name': 'v1.13.0'}
        if path.endswith('attach_files?per_page=100'):
            return [{'id': name, 'name': name} for name in self.assets]
        if method == 'DELETE':
            self.assets.pop(path.rsplit('/', 1)[-1])
            return None
        if path.startswith('https://gitee.com/'):
            name = urllib.parse.urlsplit(path).path.rsplit('/', 1)[-1]
            if self.corrupt and '?verify=' in path and name.endswith('.002'):
                return b'corrupt segment'
            return self.assets[name]
        raise AssertionError('Unexpected publishing operation ' + path)

    def upload(self, path, token, data, name='ShotInk-portable.zip'):
        if self.failure and name == 'ShotInk-portable-lite.zip':
            self.failure = False
            raise OSError('Simulated upload failure')
        self.assets[name] = data

    def publish(self):
        previous = sys.argv
        sys.argv = ['publisher', '--archive', str(self.full), '--lite-archive', str(self.lite)]
        try:
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                publisher.main()
        finally:
            sys.argv = previous

    def test_two_verified_parts_and_legacy_single_package(self):
        self.publish()
        index = json.loads((self.root / 'updates/latest.json').read_text(encoding='utf-8'))
        lite = json.loads((self.root / 'updates/latest-lite.json').read_text(encoding='utf-8'))
        self.assertEqual(len(index['parts']), 2)
        self.assertNotIn('parts', lite)
        assembled = b''
        for number, part in enumerate(index['parts'], 1):
            name = 'ShotInk-portable.zip.' + str(number).zfill(3)
            raw = self.assets[name]
            self.assertLessEqual(len(raw), publisher.MAX_ATTACHMENT_SIZE)
            self.assertEqual(part['size'], len(raw))
            self.assertEqual(part['sha256'], hashlib.sha256(raw).hexdigest())
            self.assertEqual((self.root / name).read_bytes(), raw)
            assembled += raw
        self.assertEqual(assembled, self.full.read_bytes())
        self.assertEqual(index['sha256'], hashlib.sha256(assembled).hexdigest())
        self.assertEqual(self.assets['ShotInk-portable-lite.zip'], self.lite.read_bytes())

    def assert_rollback(self):
        old = {'ShotInk-portable.zip.001': b'old first', 'ShotInk-portable.zip.002': b'old second', 'ShotInk-portable-lite.zip': b'old lite'}
        self.assets.update(old)
        for name in ['latest.json', 'latest-lite.json']:
            (self.root / 'updates' / name).write_bytes(b'previous index')
        with self.assertRaises((OSError, RuntimeError)):
            self.publish()
        self.assertEqual(self.assets, old)
        for name in ['latest.json', 'latest-lite.json']:
            self.assertEqual((self.root / 'updates' / name).read_bytes(), b'previous index')

    def test_failed_upload_restores_both_parts_and_indexes(self):
        self.failure = True
        self.assert_rollback()

    def test_corrupt_anonymous_part_keeps_previous_publication(self):
        self.corrupt = True
        self.assert_rollback()


if __name__ == '__main__':
    unittest.main()

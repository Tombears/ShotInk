"""Publish the verified portable ZIP and update index to Gitee. Never ship credentials.

GITEE_TOKEN can be supplied by CI; Windows publishers may use the saved DPAPI token.
The Git repository index is committed separately after a successful asset verification.
"""
import argparse, base64, ctypes, hashlib, json, os, pathlib, re, subprocess, sys, urllib.error, urllib.parse, urllib.request, uuid, zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
REPOSITORY = 'clear_wind_and_rain/ShotInk'
API = 'https://gitee.com/api/v5/repos/' + REPOSITORY

def saved_token():
    token = os.environ.get('GITEE_TOKEN')
    if token:
        return token
    if os.name != 'nt':
        raise RuntimeError('GITEE_TOKEN is required for Gitee publication.')
    class Blob(ctypes.Structure):
        _fields_ = [('size', ctypes.c_ulong), ('data', ctypes.POINTER(ctypes.c_ubyte))]
    raw = bytes.fromhex(pathlib.Path('E:/DevData/Gitee/token.dpapi').read_text(encoding='ascii').strip())
    buffer = (ctypes.c_ubyte * len(raw)).from_buffer_copy(raw)
    source, result = Blob(len(raw), buffer), Blob()
    if not ctypes.windll.crypt32.CryptUnprotectData(ctypes.byref(source), None, None, None, None, 0, ctypes.byref(result)):
        raise RuntimeError('Cannot decrypt the saved Gitee token for this Windows account.')
    try:
        return ctypes.string_at(result.data, result.size).decode('utf-16-le')
    finally:
        ctypes.memset(result.data, 0, result.size)
        ctypes.windll.kernel32.LocalFree(result.data)

def request(path, token='', method='GET', body=None, content_type=None, binary=False, content_length=None):
    url = path if path.startswith('https://') else API + path
    headers = {'User-Agent': 'ShotInk-Publisher'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if content_type:
        headers['Content-Type'] = content_type
    if content_length is not None:
        headers['Content-Length'] = str(content_length)
    # Authenticated API operations never follow a redirect carrying credentials.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args, **kwargs):
            return None
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    try:
        response = opener.open(urllib.request.Request(url, body, headers, method=method), timeout=900 if content_length is not None else 180)
    except urllib.error.HTTPError as error:
        if error.code == 404 and method == 'GET':
            return None
        if binary and error.code in (301, 302, 303, 307, 308):
            location = error.headers.get('Location', '')
            if not location.startswith('https://'):
                raise RuntimeError('Insecure Gitee download redirect') from None
            response = urllib.request.build_opener(urllib.request.ProxyHandler({})).open(location, timeout=180)
        else:
            raise RuntimeError('Gitee HTTP ' + str(error.code) + ' during ' + method + ' ' + urllib.parse.urlsplit(url).path) from None
    with response:
        data = response.read()
        return data if binary else json.loads(data) if data else None

def form(path, token, method, data):
    return request(path, token, method, urllib.parse.urlencode(data).encode(), 'application/x-www-form-urlencoded')

def upload(path, token, data, name="ShotInk-portable.zip"):
    boundary = 'ShotInk' + uuid.uuid4().hex
    prefix = ('--' + boundary + '\r\nContent-Disposition: form-data; name="file"; filename="' + name + '"\r\nContent-Type: application/zip\r\n\r\n').encode()
    suffix = ('\r\n--' + boundary + '--\r\n').encode()
    # Keep a fixed Content-Length while limiting each TLS write on slow connections.
    def parts():
        yield prefix
        for offset in range(0, len(data), 65536):
            yield memoryview(data)[offset:offset + 65536]
            sent = min(offset + 65536, len(data))
            if sent % (8 * 1024 * 1024) == 0 or sent == len(data):
                print('Sent ' + str(sent) + ' / ' + str(len(data)) + ' package bytes.', file=sys.stderr, flush=True)
        yield suffix
    return request(path, token, 'POST', parts(), 'multipart/form-data; boundary=' + boundary, content_length=len(prefix) + len(data) + len(suffix))

def package_info(path, flavor, version, notes):
    raw = pathlib.Path(path).read_bytes()
    name = 'ShotInk-portable-lite.zip' if flavor == 'lite' else 'ShotInk-portable.zip'
    with zipfile.ZipFile(path) as package:
        names = [name.replace('\\', '/') for name in package.namelist()]
        revision = package.read('ShotInk.build.sha256').decode('utf-8-sig').strip()
        if not re.fullmatch('[a-fA-F0-9]{64}', revision):
            raise RuntimeError('Portable ZIP omitted its build revision.')
        if package.read('ShotInk.flavor').decode('utf-8-sig').strip() != flavor:
            raise RuntimeError('Portable ZIP edition mismatch.')
        required = ['ShotInk.exe', 'update-portable.ps1', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll']
        if flavor == 'ocr': required.append('ocr/ShotInk.OcrHost.exe')
        if any(item not in names for item in required): raise RuntimeError('Portable ZIP omitted required components.')
        if any(item.lower().startswith('docs/') for item in names): raise RuntimeError('Portable ZIP retained docs.')
        if flavor == 'lite' and any(item.lower().startswith('ocr/') for item in names): raise RuntimeError('Lite portable ZIP retained OCR.')
    digest = hashlib.sha256(raw).hexdigest()
    url = 'https://gitee.com/' + REPOSITORY + '/releases/download/v' + version + '/' + name
    index = dict(version=version, revision=revision, sha256=digest, size=len(raw), url=url, notes=notes)
    return dict(name=name, raw=raw, index=index, index_name='latest-lite.json' if flavor == 'lite' else 'latest.json')

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--archive', required=True)
    parser.add_argument('--lite-archive', required=True)
    args = parser.parse_args()
    version = re.search(r'AssemblyInformationalVersion\("(\d+\.\d+\.\d+)"\)', (ROOT / 'src/AssemblyInfo.cs').read_text(encoding='utf-8')).group(1)
    tag = 'v' + version
    notes = (ROOT / 'releases' / (tag + '.md')).read_text(encoding='utf-8')
    packages = [package_info(args.archive, 'ocr', version, notes), package_info(args.lite_archive, 'lite', version, notes)]
    token = saved_token()
    release = request('/releases/tags/' + tag, token)
    metadata = {'tag_name': tag, 'name': version, 'body': notes, 'prerelease': 'false'}
    if release is None:
        commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
        release = form('/releases', token, 'POST', dict(metadata, target_commitish=commit))
    files_path = '/releases/' + str(release['id']) + '/attach_files'
    assets = request(files_path + '?per_page=100', token) or []
    backups, changed = {}, []
    for package in packages:
        existing = next((asset for asset in assets if asset['name'] == package['name']), None)
        previous = request(package['index']['url'], binary=True) if existing else None
        if existing and previous is None: raise RuntimeError('Cannot back up the existing Gitee package.')
        backups[package['name']] = (existing, previous)
    try:
        for package in packages:
            name, index = package['name'], package['index']
            existing, previous = backups[name]
            if previous is None or hashlib.sha256(previous).hexdigest() != index['sha256']:
                changed.append(package)
                if existing: request(files_path + '/' + str(existing['id']), token, 'DELETE')
                print('Uploading ' + name, file=sys.stderr, flush=True)
                upload(files_path, token, package['raw'], name)
            print('Verifying anonymous download: ' + name, file=sys.stderr, flush=True)
            check = request(index['url'] + '?verify=' + uuid.uuid4().hex, binary=True)
            if check is None or hashlib.sha256(check).hexdigest() != index['sha256']:
                raise RuntimeError('Anonymous Gitee download checksum mismatch: ' + name)
        form('/releases/' + str(release['id']), token, 'PATCH', metadata)
    except Exception:
        for package in reversed(changed):
            name = package['name']
            current = request(files_path + '?per_page=100', token) or []
            for asset in current:
                if asset['name'] == name: request(files_path + '/' + str(asset['id']), token, 'DELETE')
            previous = backups[name][1]
            if previous is not None: upload(files_path, token, previous, name)
        raise
    for package in packages:
        destination = ROOT / 'updates' / package['index_name']
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(json.dumps(package['index'], ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(tag=tag, verified=True, packages=[dict(name=p['name'], **p['index']) for p in packages]), ensure_ascii=False))

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(type(error).__name__ + ': ' + str(error), file=sys.stderr)
        sys.exit(1)

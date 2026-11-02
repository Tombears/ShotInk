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

def request(path, token='', method='GET', body=None, content_type=None, binary=False):
    url = path if path.startswith('https://') else API + path
    headers = {'User-Agent': 'ShotInk-Publisher'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if content_type:
        headers['Content-Type'] = content_type
    # Authenticated API operations never follow a redirect carrying credentials.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args, **kwargs):
            return None
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    try:
        response = opener.open(urllib.request.Request(url, body, headers, method=method), timeout=180)
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

def upload(path, token, data):
    boundary = 'ShotInk' + uuid.uuid4().hex
    prefix = ('--' + boundary + '\r\nContent-Disposition: form-data; name="file"; filename="ShotInk-portable.zip"\r\nContent-Type: application/zip\r\n\r\n').encode()
    body = prefix + data + ('\r\n--' + boundary + '--\r\n').encode()
    return request(path, token, 'POST', body, 'multipart/form-data; boundary=' + boundary)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--archive', required=True)
    args = parser.parse_args()
    archive = pathlib.Path(args.archive)
    version = re.search(r'AssemblyInformationalVersion\("(\d+\.\d+\.\d+)"\)', (ROOT / 'src/AssemblyInfo.cs').read_text(encoding='utf-8')).group(1)
    tag = 'v' + version
    raw = archive.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    with zipfile.ZipFile(archive) as package:
        revision = package.read('ShotInk.build.sha256').decode('utf-8-sig').strip()
        if not re.fullmatch('[a-fA-F0-9]{64}', revision):
            raise RuntimeError('Portable ZIP omitted its build revision.')
        for name in ('ShotInk.exe', 'ocr/ShotInk.OcrHost.exe', 'update-portable.ps1'):
            if name not in package.namelist():
                raise RuntimeError('Portable ZIP omitted ' + name)
    notes = (ROOT / 'releases' / (tag + '.md')).read_text(encoding='utf-8')
    token = saved_token()
    release = request('/releases/tags/' + tag, token)
    metadata = {'name': version, 'body': notes, 'prerelease': 'false'}
    if release is None:
        commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
        release = form('/releases', token, 'POST', dict(metadata, tag_name=tag, target_commitish=commit))
    files_path = '/releases/' + str(release['id']) + '/attach_files'
    assets = request(files_path + '?per_page=100', token) or []
    existing = next((asset for asset in assets if asset['name'] == 'ShotInk-portable.zip'), None)
    url = 'https://gitee.com/' + REPOSITORY + '/releases/download/' + tag + '/ShotInk-portable.zip'
    previous = request(url, binary=True) if existing else None
    if existing and previous is None:
        raise RuntimeError('Cannot back up the existing Gitee package; leaving it unchanged.')
    if previous is None or hashlib.sha256(previous).hexdigest() != digest:
        if existing:
            request(files_path + '/' + str(existing['id']), token, 'DELETE')
        try:
            upload(files_path, token, raw)
            check = request(url + '?verify=' + uuid.uuid4().hex, binary=True)
            if check is None or hashlib.sha256(check).hexdigest() != digest:
                raise RuntimeError('Anonymous Gitee download checksum mismatch.')
        except Exception:
            if previous is not None:
                current = request(files_path + '?per_page=100', token) or []
                for asset in current:
                    if asset['name'] == 'ShotInk-portable.zip':
                        request(files_path + '/' + str(asset['id']), token, 'DELETE')
                upload(files_path, token, previous)
            raise
    form('/releases/' + str(release['id']), token, 'PATCH', metadata)
    # The index becomes visible only after the exact ZIP is anonymously downloadable.
    index = dict(version=version, revision=revision, sha256=digest, size=len(raw), url=url, notes=notes)
    destination = ROOT / 'updates/latest.json'
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(index, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(tag=tag, url=url, sha256=digest, verified=True, index=str(destination)), ensure_ascii=False))

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(type(error).__name__ + ': ' + str(error), file=sys.stderr)
        sys.exit(1)

"""Artifact-level checks only. This is not a C#/Razor compiler or runtime test."""
import json, pathlib, re, xml.etree.ElementTree as ET
root = pathlib.Path(__file__).resolve().parents[1]
for file in root.rglob('*.json'):
    if 'obj' not in file.parts and 'bin' not in file.parts: json.loads(file.read_text())
for file in [root/'Osynix.Ats.slnx', *root.glob('src/**/*.csproj'), *root.glob('tests/**/*.csproj')]: ET.parse(file)
config=json.loads((root/'src/Osynix.Ats/appsettings.json').read_text())
assert config['OpenAI']['ApiKey']=='' and config['Bootstrap']['Password']==''
routes=[]
for file in root.glob('src/**/*.razor'):
    for route in re.findall(r'@page\s+"([^"]+)"',file.read_text()):
        assert route not in routes, f'Duplicate route {route}'
        routes.append(route)
    if '/Pages/' in str(file) and file.name!='Error.razor':
        assert '[Authorize(' in file.read_text(), f'Missing authorization: {file}'
assert len(routes)>=10
for file in root.glob('src/**/*'):
    if file.is_file():
        content=file.read_text()
        assert not re.search(r'sk-(?:proj-)?[A-Za-z0-9_-]{20,}',content), f'Possible key in {file}'
        assert 'NotImplementedException' not in content
print(f'PASS: JSON/XML parse, {len(routes)} unique routes, page authorization declarations, empty secrets, no NotImplementedException.')
print('NOT VERIFIED: C#/Razor compilation, NuGet restore, tests, UI, AI calls, PDFs, actual workbook import.')

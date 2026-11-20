"""Create and read back a new portable ZIP from an audited current build."""
import argparse
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--folder', required=True)
    parser.add_argument('--audit', required=True)
    parser.add_argument('--label', default='integration-preview', help='Distinct local package label; existing outputs are never replaced')
    parser.add_argument('--desktop-report', help='Optional completed, isolated physical-desktop acceptance receipt')
    parser.add_argument('--verified-local', action='store_true', help='Mark a locally verified release; requires a matching physical-desktop receipt')
    args = parser.parse_args()
    if not re.fullmatch(r'[a-z0-9]+(?:-[a-z0-9]+)*', args.label):
        parser.error('Package label must contain lowercase ASCII words separated by hyphens')
    desktop = None
    if args.desktop_report:
        desktop = json.loads((ROOT/args.desktop_report).read_text(encoding='utf-8-sig'))
        if desktop.get('passed') is not True or not desktop.get('physicalInput') or not desktop.get('isolatedData'):
            parser.error('A completed isolated physical-desktop acceptance receipt is required')
    if args.verified_local and desktop is None:
        parser.error('--verified-local requires --desktop-report')
    folder = (ROOT/args.folder).resolve()
    if not folder.is_relative_to(ROOT/'release') or not folder.is_dir():
        parser.error('Only a new build under release/ may be packaged')
    audit = json.loads((ROOT/args.audit).read_text(encoding='utf-8-sig'))
    if audit.get('passed') is not True or Path(audit['root']).resolve() != folder:
        parser.error('An actual PackageAudit receipt for this folder is required')
    version = ET.parse(ROOT/'src/Taskboard.Desktop/Taskboard.Desktop.csproj').findtext('PropertyGroup/Version')
    archive = ROOT/'release'/f'MoyuDesk-{version}-{args.label}-win-x64.zip'
    manifest = ROOT/'docs'/f'BUILD-INFO-{version}-{args.label}.json'
    sidecar = Path(str(archive)+'.sha256')
    if any(p.exists() for p in (archive, manifest, sidecar)):
        parser.error('Existing ZIP, SHA256 or manifest must not be overwritten')
    files = []
    for path in sorted(folder.rglob('*')):
        if not path.is_file(): continue
        relative = path.relative_to(folder).as_posix()
        if any(p.lower() in {'data','configuration','preserved-data','.git','test-output','history'} for p in path.relative_to(folder).parts):
            parser.error('Data or history directory in portable package: '+relative)
        if path.name in {'board.json','board.json.bak','launch-settings.json'}:
            parser.error('User state in portable package: '+relative)
        data = path.read_bytes()
        if audit.get('fileHashes',{}).get(relative) != digest(data):
            parser.error('Package bytes changed or lack a current PackageAudit fingerprint: '+relative)
        files.append({'Path':relative,'Bytes':len(data),'SHA256':digest(data)})
    if len(files) != audit['files']:
        parser.error('Package changed after its PE/resource audit')
    if desktop and desktop['testedAssemblySHA256'] != audit['fileHashes'].get('Taskboard.dll'):
        parser.error('Desktop-tested assembly differs from the assembly being packaged')
    with ZipFile(archive, 'x', compression=ZIP_DEFLATED, compresslevel=6) as zip_file:
        for row in files: zip_file.write(folder/row['Path'], row['Path'])
    with ZipFile(archive) as zip_file:
        if sorted(zip_file.namelist()) != sorted(row['Path'] for row in files):
            raise RuntimeError('ZIP entry list differs from portable directory')
        for row in files:
            data = zip_file.read(row['Path'])
            if len(data) != row['Bytes'] or digest(data) != row['SHA256']:
                raise RuntimeError('ZIP read-back mismatch: '+row['Path'])
    archive_hash = digest(archive.read_bytes())
    sidecar.write_text(archive_hash+'  '+archive.name+'\n',encoding='utf-8')
    sources=[]
    for path in sorted((ROOT/'src').rglob('*')):
        if not path.is_file() or {'bin','obj'}.intersection(path.relative_to(ROOT/'src').parts): continue
        sources.append({'Path':path.relative_to(ROOT).as_posix(),'Bytes':path.stat().st_size,'SHA256':digest(path.read_bytes())})
    result = dict(Version=version, BuildDate=datetime.now(timezone.utc).isoformat(), Runtime='win-x64 self-contained',
                  Published=False, Preview=not args.verified_local,
                  PreviewReason=None if args.verified_local else 'Local preview; consult bundled acceptance for the validated scope',
                  DesktopVerificationComplete=bool(desktop), DesktopVerificationReceipt=args.desktop_report,
                  DesktopVerificationScope=desktop['scope'] if desktop else None, PackagingPassed=True,
                  Entry=(folder/'MoyuDesk.exe').relative_to(ROOT).as_posix(),
                  Archive=archive.relative_to(ROOT).as_posix(),ArchiveSHA256=archive_hash,Bytes=archive.stat().st_size,
                  FileCount=len(files),Files=files,Sources=sources,ZipAllFilesVerified=True,ContainsData=False,
                  AuditReceipt=args.audit,ValidationScope='Package PE metadata/resources/runtime and full ZIP read-back; physical-desktop scope is recorded separately when supplied')
    manifest.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k not in {'Files','Sources'}},ensure_ascii=True))


if __name__ == '__main__': main()

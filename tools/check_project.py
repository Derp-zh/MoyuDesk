"""Check production module ownership and exclusion of retired implementations."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def check(output):
    errors, modules = [], {}
    for project in ('Taskboard.Desktop', 'Taskboard.Core'):
        base = ROOT / 'src' / project
        owners = {}
        for props in sorted((base / 'Modules').glob('*.props')):
            names = []
            for element in ET.parse(props).iter('Compile'):
                name = element.attrib['Include'].replace('$(MSBuildThisFileDirectory)../', '')
                if name in owners:
                    errors.append(f'Duplicate owner: {project}/{name}')
                owners[name] = props.stem
                if not (base / name).is_file():
                    errors.append(f'Missing module source: {project}/{name}')
                names.append(name)
            modules[project + '/' + props.stem] = names
        actual = {p.relative_to(base).as_posix() for p in base.rglob('*.cs')
                  if not {'bin', 'obj'}.intersection(p.relative_to(base).parts)}
        if actual != set(owners):
            errors.append(f'Unowned or stale source in {project}: {sorted(actual ^ set(owners))}')
        if ET.parse(base / (project + '.csproj')).findtext('PropertyGroup/EnableDefaultCompileItems') != 'false':
            errors.append(f'Implicit source inclusion: {project}')
        imports = {e.get('Project') for e in ET.parse(base / (project + '.csproj')).iter('Import')}
        if 'Modules/*.props' not in imports:
            errors.append(f'Missing module import: {project}')
    forbidden = ('CodexRpc', 'CodexDesktopBridge', 'CodexPendingBridge', 'CodexLocalEvents',
                 'CodexLocalState', 'CodexReadBudget', 'CodexTurnTracker', 'CodexWindowsNotifications', 'CodexNotificationRules')
    for base in (ROOT / 'src').iterdir():
        for file in base.rglob('*.cs'):
            if {'bin', 'obj'}.intersection(file.relative_to(base).parts):
                continue
            if file.stem in forbidden or file.stem.endswith('Checks'):
                errors.append('Retired/test source in production: ' + str(file))
            text = file.read_text(encoding='utf-8-sig')
            for token in ('thread/subscribe', 'thread/loaded/list', '--app-server', 'NamedPipeClientStream', 'ClientWebSocket'):
                if token in text:
                    errors.append(f'Retired transport token {token}: {file.name}')
    report = dict(passed=not errors, modules=modules, module_count=len(modules),
                  source_count=sum(map(len, modules.values())), errors=errors,
                  scope='Static module ownership and production exclusion check.')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'modules'}, ensure_ascii=False))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', default='test-output/project-check.json')
    args = parser.parse_args()
    output = (ROOT / args.report).resolve()
    if not output.is_relative_to(ROOT / 'test-output'):
        parser.error('Report must stay under test-output/')
    raise SystemExit(check(output))

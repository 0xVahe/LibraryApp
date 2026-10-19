"""Checks the EF Core migrations of a pull request against its base branch.

Usage: check_migrations.py <base> <head>   (any git tree-ish: a commit, a branch, a tree id)

In CI, <base> is the tip of the target branch and <head> the merge result of the pull request, so the comparison sees
exactly what the merge would bring. Three rules:
  1. A migration that is already in the base branch is never changed or deleted: databases have applied it.
  2. Every new migration is newer than every migration in the base branch. An older one would sit before a migration
     that another pull request merged meanwhile, while its model snapshot does not know that migration; the Integration
     Owner then re-creates it so it is the last one.
  3. Every new migration has its .Designer.cs file (EF Core needs both).
"""
import re
import subprocess
import sys

MIGRATIONS = 'src/LibraryApp.Infrastructure/Persistence/Migrations/'
NAME = re.compile(r'^(\d{14})_(\w+)\.cs$')


def migrations(treeish):
    """{file name: blob id} of the migration files (without Designer files and the snapshot) in treeish."""
    out = subprocess.run(['git', 'ls-tree', '-r', treeish, '--', MIGRATIONS],
                         capture_output=True, text=True, check=True).stdout
    files = {}
    for line in out.splitlines():
        meta, path = line.split('\t', 1)
        name = path[len(MIGRATIONS):]
        files[name] = meta.split()[2]
    return files


def main(base, head):
    base_files, head_files = migrations(base), migrations(head)
    base_mig = {n: b for n, b in base_files.items() if NAME.match(n) and not n.endswith('.Designer.cs')}
    head_mig = {n: b for n, b in head_files.items() if NAME.match(n) and not n.endswith('.Designer.cs')}
    errors = []

    for name, blob in sorted(base_mig.items()):
        if name not in head_mig:
            errors.append(f'{name} is in the base branch and is deleted here. A merged migration is never removed; '
                          'add a new migration that reverts it instead.')
        elif head_mig[name] != blob:
            errors.append(f'{name} is in the base branch and is changed here. A merged migration is never edited; '
                          'add a new migration instead.')

    new = sorted(n for n in head_mig if n not in base_mig)
    latest = max((NAME.match(n).group(1) for n in base_mig), default='0' * 14)
    for name in new:
        stamp = NAME.match(name).group(1)
        if stamp <= latest:
            errors.append(f'{name} is older than {latest}, the newest migration in the base branch. Update the branch '
                          'from the base branch and re-create the migration so it is the last one (Integration Owner).')
        if name[:-3] + '.Designer.cs' not in head_files:
            errors.append(f'{name} has no {name[:-3]}.Designer.cs.')

    print(f'Migrations in the base branch: {len(base_mig)} (newest {latest}); new here: {len(new)}'
          + (''.join(f'\n  {n}' for n in new)))
    for error in errors:
        print(f'::error::{error}')
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1], sys.argv[2]))

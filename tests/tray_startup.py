"""Cold-tray integration regression for an isolated MYKEY Release build on Windows."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import json
import os
from pathlib import Path

import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parent.parent
user = c.WinDLL('user32', use_last_error=True)
ENUM = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
user.EnumWindows.argtypes = [ENUM, w.LPARAM]
user.GetWindowThreadProcessId.argtypes = [w.HWND, c.POINTER(w.DWORD)]
user.GetWindowTextW.argtypes = [w.HWND, w.LPWSTR, c.c_int]
user.IsWindowVisible.argtypes = [w.HWND]
user.PostMessageW.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM]
user.GetClassNameW.argtypes = [w.HWND, w.LPWSTR, c.c_int]
user.SendMessageTimeoutW.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM, w.UINT, w.UINT, c.POINTER(c.c_size_t)]
user.SendMessageTimeoutW.restype = w.LPARAM


def windows(pid, tray=False):
    result = []

    @ENUM
    def collect(hwnd, _):
        owner = w.DWORD()
        user.GetWindowThreadProcessId(hwnd, c.byref(owner))
        if owner.value == pid:
            name = c.create_unicode_buffer(256)
            if tray:
                user.GetClassNameW(hwnd, name, len(name))
                matches = name.value.startswith('WindowsForms10.Window.')
            else:
                user.GetWindowTextW(hwnd, name, len(name))
                matches = name.value == 'MYKEY'
            if matches:
                result.append((hwnd, bool(user.IsWindowVisible(hwnd))))
        return True

    user.EnumWindows(collect, 0)
    return result


def wait(proc, description, predicate):
    deadline = time.monotonic() + 12
    while time.monotonic() < deadline:
        assert proc.poll() is None, f'MYKEY test PID {proc.pid} exited: {proc.returncode}'
        if result := predicate():
            return result
        time.sleep(.05)
    raise AssertionError(f'Timed out waiting for {description}')


def check(exe, flag):
    data = ROOT / 'Artifacts' / 'wpf-optimization' / ('tray-' + uuid.uuid4().hex)
    data.mkdir(parents=True)
    (data / 'settings.json').write_text(json.dumps({'CheckUpdatesOnStartup': False, 'CloseToTray': False}))
    cmd = [str(exe), '--test-mode', '--data-dir', str(data)]
    env = dict(os.environ, DOTNET_ROOT=str(ROOT / '.dotnet'), DOTNET_ROOT_X64=str(ROOT / '.dotnet'))
    proc = subprocess.Popen([*cmd, flag], cwd=exe.parent, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    try:
        time.sleep(2)
        assert proc.poll() is None, f'Tray launch exited: {proc.returncode}'
        assert not windows(proc.pid), f'{flag} created a main window before activation'
        assert not (data / 'mykey.db').exists(), f'{flag} loaded the database before activation'
        print(f'PASS: {flag} stayed alive with no main window and no database', flush=True)
        subprocess.run(cmd, check=True, cwd=exe.parent, env=env, timeout=10, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
        hwnd, _ = wait(proc, 'original PID showing its main window', lambda: next((w for w in windows(proc.pid) if w[1]), None))
        assert (data / 'mykey.db').exists(), f'{flag} did not initialize the test database on activation'
        print(f'PASS: {flag} second launch opened the main window on original PID', flush=True)
        assert user.PostMessageW(hwnd, 0x0010, 0, 0), 'Could not deliver WM_CLOSE to owned test window'
        assert proc.wait(timeout=10) == 0, 'MYKEY normal close did not exit cleanly'
        print(f'PASS: {flag} normal close exited cleanly', flush=True)
    finally:
        if proc.poll() is None:
            proc.terminate()
            proc.wait(timeout=10)


def check_maintenance(exe, runner):
    data = ROOT / 'Artifacts' / 'wpf-optimization' / ('maintenance-' + uuid.uuid4().hex)
    data.mkdir(parents=True)
    (data / 'settings.json').write_text(json.dumps({'CheckUpdatesOnStartup': False, 'CloseToTray': False}))
    cmd = [str(exe), '--test-mode', '--data-dir', str(data)]
    env = dict(os.environ, DOTNET_ROOT=str(ROOT / '.dotnet'), DOTNET_ROOT_X64=str(ROOT / '.dotnet'))

    def launch(extra=()):
        return subprocess.Popen([*cmd, *extra], cwd=exe.parent, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)

    def finish(proc):
        if proc.poll() is None:
            proc.terminate()
            proc.wait(timeout=10)

    fixture = subprocess.Popen(
        [str(ROOT / '.dotnet/dotnet.exe'), str(runner), '--test-mode', '--data-dir', str(data), '--maintenance-fixture'],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, env=env, cwd=ROOT)
    proc = None
    try:
        assert fixture.stdout is not None and fixture.stdin is not None
        assert fixture.stdout.readline().strip() == 'LOCKED', 'Could not acquire encrypted fixture lock'
        started = time.monotonic()
        proc = launch(['--tray'])
        tray_windows = wait(proc, 'WinForms tray message window', lambda: windows(proc.pid, tray=True))
        time.sleep(max(0, started + 61.2 - time.monotonic()))
        assert not windows(proc.pid), 'Maintenance unexpectedly created the main window'
        for hwnd, _ in tray_windows:
            result = c.c_size_t()
            assert user.SendMessageTimeoutW(hwnd, 0, 0, 0, 0x0003, 250, c.byref(result)), \
                'Tray UI message loop stalled while the maintenance database was locked'
        print('PASS: tray still dispatches Windows messages during SQLite lock contention', flush=True)
        output, _ = fixture.communicate('release\n', timeout=15)
        assert fixture.returncode == 0 and output.strip() == 'CLEANUP_OK', output
        assert not windows(proc.pid), 'Cleanup should not open the main window'
        print('PASS: isolated cold-tray cleanup removed the expired card and retained the recent card', flush=True)
    finally:
        finish(fixture)
        if proc is not None:
            finish(proc)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('test_exe', type=Path)
    parser.add_argument('--maintenance', action='store_true', help='Also exercise the real one-minute cleanup timer')
    parser.add_argument('--runner', type=Path, default=ROOT / 'tests/bin/Release/net8.0-windows/MyKey.Tests.dll')
    options = parser.parse_args()
    exe = options.test_exe.resolve(strict=True)
    private_app_dir = (Path(os.environ['LOCALAPPDATA']) / 'Programs' / 'MYKEY').resolve()
    assert not exe.is_relative_to(private_app_dir), 'Refusing to launch the installed MYKEY executable'
    for flag in ('--tray', '--silent'):
        check(exe, flag)
    if options.maintenance:
        check_maintenance(exe, options.runner.resolve(strict=True))

#!/usr/bin/env python3
"""Upgrade GameNet 98 CS2 LAN from v0.5.2 to dedicated-server v0.5.3."""
from __future__ import annotations
import argparse
import base64
from pathlib import Path
import zipfile

PART_NAMES = [
    "GameNet98-CS2-LAN-source-v040.part01.txt",
    "GameNet98-CS2-LAN-source-v040.part02.txt",
    "GameNet98-CS2-LAN-source-v040.part03.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s01.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s02.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s03.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s04.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s05.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s06.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s07.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s08-a.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s08-b.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s08-c1.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s08-c2.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s08-d.txt",
    "GameNet98-CS2-LAN-source-v040.part04-s08-e.txt",
    "GameNet98-CS2-LAN-source-v040.part05.txt",
    "GameNet98-CS2-LAN-source-v040.part06.txt",
    "GameNet98-CS2-LAN-source-v040.part07.txt",
    "GameNet98-CS2-LAN-source-v040.part08.txt",
]
FIXED_CHUNK_SIZES = [4000, 4000, 4000] + [500] * 7 + [100] * 6 + [4000] * 3

def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one exact match, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")

def patch_source(src: Path) -> None:
    core = src / "gamenet98_core.py"
    ui = src / "gamenet98_lan.pyw"
    tests = src / "tests" / "test_core.py"
    iss = src / "GameNet98-CS2-LAN-Offline.iss"
    fa = src / "README_FA.md"
    readme = src / "README.md"
    core_text = core.read_text(encoding="utf-8")
    if 'APP_VERSION = "0.5.3-beta"' in core_text:
        print("Source archive is already at v0.5.3; preserving it.")
        return
    if 'APP_VERSION = "0.5.2-beta"' not in core_text:
        raise RuntimeError("Expected v0.5.2 source; refusing to patch an unknown version.")

    replace_once(core, 'APP_VERSION = "0.5.2-beta"', 'APP_VERSION = "0.5.3-beta"', "core version")
    replace_once(core,
        '    """Write a listen-server config next to the selected CS2 copy\'s cfg folder."""',
        '    """Write server settings for a dedicated CS2 server process."""',
        "host config docstring")
    replace_once(core, '    game_type, game_mode = MODES[mode]\n', '', "remove startup modes from cfg")
    replace_once(core,
        '        f"game_type {game_type}",\n        f"game_mode {game_mode}",\n        f"map {map_name}",\n',
        '', "remove map/mode from cfg")
    replace_once(core, '        "mp_restartgame 1",\n', '', "remove restart command from startup cfg")

    old_args = '''def build_host_arguments(extra_args: str, port: int, cfg_name: str = "gamenet98_lan_host.cfg") -> list[str]:
    return [*split_windows_arguments(extra_args), "-port", str(validate_port(str(port))), "+exec", cfg_name]
'''
    new_args = '''def build_host_arguments(
    extra_args: str,
    port: int,
    cfg_name: str = "gamenet98_lan_host.cfg",
    map_name: str = "de_dust2",
    mode: str = "Casual",
    host_ip: str = "127.0.0.1",
) -> list[str]:
# Launch a real CS2 dedicated server, not the regular playable client.
    checked_port = validate_port(str(port))
    checked_ip = validate_ipv4(host_ip)
    if map_name not in MAPS:
        raise ValueError("نقشهٔ انتخاب‌شده پشتیبانی نمی‌شود.")
    if mode not in MODES:
        raise ValueError("حالت بازی انتخاب‌شده پشتیبانی نمی‌شود.")
    game_type, game_mode = MODES[mode]
    user_args = [arg for arg in split_windows_arguments(extra_args) if arg.casefold() != "-dedicated"]
    return [
        "-dedicated", *user_args,
        "-ip", checked_ip,
        "-port", str(checked_port),
        "-maxplayers", str(MAX_PLAYERS),
        "+sv_lan", "1",
        "+game_type", str(game_type),
        "+game_mode", str(game_mode),
        "+map", map_name,
        "+exec", cfg_name,
    ]
'''
    replace_once(core, old_args, new_args, "dedicated host arguments")
    replace_once(ui,
        'launch_executable(exe,build_host_arguments(self.args.get(),port,cfg.name),cwd=exe.parent)',
        'launch_executable(exe,build_host_arguments(self.args.get(),port,cfg.name,self.map.get(),self.mode.get(),ip),cwd=exe.parent)',
        "pass host settings to server args")
    replace_once(ui, 'self.clear(); tk.Label(self.main,text="در حال ساخت سرور…"',
        'self.clear(); tk.Label(self.main,text="در حال راه‌اندازی سرور اختصاصی…"', "host page title")
    replace_once(ui, 'برنامه به‌طور خودکار منتظر پاسخ سرور می‌ماند؛ لازم نیست دستی «آماده» را بزنی.',
        'سرور اختصاصی جدا از بازی بازیکن اجرا می‌شود. بعد از آماده‌شدن، CS2 را جداگانه از Steam باز کن.',
        "host page instructions")

    replace_once(tests, '        host = build_host_arguments("-console", 27015)\n        self.assertEqual(host, ["-console", "-port", "27015", "+exec", "gamenet98_lan_host.cfg"])',
        '        host = build_host_arguments("-console", 27015, host_ip="192.168.0.9")\n        self.assertEqual(host, ["-dedicated", "-console", "-ip", "192.168.0.9", "-port", "27015", "-maxplayers", "16", "+sv_lan", "1", "+game_type", "0", "+game_mode", "0", "+map", "de_dust2", "+exec", "gamenet98_lan_host.cfg"])',
        "dedicated launch test")
    replace_once(tests,
        '            self.assertIn("game_type 0", text)\n            self.assertIn("game_mode 0", text)\n            self.assertIn("map de_dust2", text)\n',
        '            self.assertNotIn("game_type 0", text)\n            self.assertNotIn("game_mode 0", text)\n            self.assertNotIn("map de_dust2", text)\n',
        "startup settings config test")

    replace_once(iss, '#define MyAppVersion "0.5.2"', '#define MyAppVersion "0.5.3"', "installer version")
    replace_once(fa, 'نسخهٔ آزمایشی ۰.۵.۲ — افزودن دکمهٔ بستن کامل سرور.',
        'نسخهٔ آزمایشی ۰.۵.۳ — اجرای سرور اختصاصی واقعی و دکمهٔ بستن کامل سرور.', "Persian version text")
    replace_once(fa,
        'تازه بعد از این دکمه، برنامه فایل تنظیمات میزبان را می‌سازد، بازی را اجرا می‌کند و به‌صورت خودکار پاسخ سرور را بررسی می‌کند.',
        'تازه بعد از این دکمه، برنامه سرور اختصاصی CS2 را اجرا می‌کند؛ وارد بازی بازیکن نمی‌شوی. وقتی وضعیت سرور آماده شد، بازی را جداگانه از Steam باز کن و به IP و پورت نشان‌داده‌شده وصل شو.',
        "Persian host workflow")
    replace_once(readme,
        '4. Choose and save the LAN adapter once. The host presses **Create Server**, chooses server name/map/mode, then presses **Start Server**. Clients press **Find Server**, select a simple Server N — IP row and press **Connect**. Hosts are sorted numerically by IP and each selected endpoint uses the port advertised by that host.',
        '4. Choose and save the LAN adapter once. The host presses **Create Server**, chooses server name/map/mode, then presses **Start Server**. The launcher starts a dedicated-server process using -dedicated; open CS2 separately from Steam to play. Clients can connect to the listed IP and port. Hosts are sorted numerically by IP.',
        "English host workflow")
    print("Patched GameNet 98 CS2 LAN source to v0.5.3 dedicated-server mode.")

def repack_and_split(src: Path, repo_root: Path) -> None:
    zip_path = repo_root / "tools" / "GameNet98-CS2-LAN-source-v053.zip"
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for file in sorted(src.rglob("*")):
            if not file.is_file() or "__pycache__" in file.parts or file.suffix == ".pyc":
                continue
            zf.write(file, (Path("GameNet98-CS2-LAN") / file.relative_to(src)).as_posix())
    encoded = base64.b64encode(zip_path.read_bytes()).decode("ascii")
    if len(encoded) <= sum(FIXED_CHUNK_SIZES):
        raise RuntimeError("Encoded source archive is unexpectedly small.")
    offset = 0
    for i, name in enumerate(PART_NAMES):
        size = FIXED_CHUNK_SIZES[i] if i < len(FIXED_CHUNK_SIZES) else 0
        chunk = encoded[offset:offset + size] if size else encoded[offset:]
        offset += len(chunk)
        if not chunk:
            raise RuntimeError(f"No data available for source chunk {name}")
        (repo_root / "tools" / name).write_text(chunk, encoding="ascii")
    if offset != len(encoded):
        raise RuntimeError(f"Source chunking did not consume all archive bytes: {offset}/{len(encoded)}")
    check = base64.b64decode("".join((repo_root / "tools" / name).read_text(encoding="ascii").strip() for name in PART_NAMES))
    if check != zip_path.read_bytes():
        raise RuntimeError("Source archive verification failed after chunking.")
    print(f"Updated v0.5.3 source ZIP: {zip_path.stat().st_size} bytes; base64 {len(encoded)} chars.")

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", required=True, type=Path)
    parser.add_argument("--repo-root", required=True, type=Path)
    args = parser.parse_args()
    if not (args.source_dir / "gamenet98_lan.pyw").is_file():
        raise SystemExit(f"Expected source directory not found: {args.source_dir}")
    patch_source(args.source_dir)
    repack_and_split(args.source_dir, args.repo_root)
    print("GameNet 98 CS2 LAN source is at v0.5.3.")

if __name__ == "__main__":
    main()

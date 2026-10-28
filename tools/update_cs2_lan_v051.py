#!/usr/bin/env python3
"""Upgrade the archived GameNet 98 CS2 LAN source to v0.5.0 and refresh repo chunks."""
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


def replace_once(path: Path, old: str, new: str, marker: str) -> None:
    text = path.read_text(encoding="utf-8")
    if any(candidate in text for candidate in marker.split("||")):
        return
    if old in text:
        path.write_text(text.replace(old, new, 1), encoding="utf-8")
        return
    raise RuntimeError(f"Cannot apply v0.5 patch to {path.name}; missing expected source marker: {marker}")


def patch_source(src: Path) -> None:
    core = src / "gamenet98_core.py"
    ui = src / "gamenet98_lan.pyw"
    tests = src / "tests" / "test_core.py"
    iss_path = src / "GameNet98-CS2-LAN-Offline.iss"

    replace_once(core, 'APP_VERSION = "0.4.0-beta"', 'APP_VERSION = "0.5.0-beta"', 'APP_VERSION = "0.5.0-beta"||APP_VERSION = "0.5.1-beta"')
    core_insert = '''def sort_discovered_servers(servers: list[dict]) -> list[dict]:
    """Sort discovered LAN hosts by numeric IPv4 address, then game port.

    This keeps the visible server numbering stable between discovery refreshes
    whenever the same set of hosts is online.
    """
    def key(server: dict) -> tuple[int, int, int, int, int]:
        ip = validate_ipv4(str(server.get("ip", "")))
        octets = tuple(int(part) for part in ip.split("."))
        port = validate_port(str(server.get("port", DEFAULT_GAME_PORT)))
        return (*octets, port)

    return sorted(servers, key=key)


def server_list_label(index: int, ip: str) -> str:
    """Show only a friendly ordinal and the host IPv4 address in the picker."""
    checked_ip = validate_ipv4(ip)
    digits = str(index).translate(str.maketrans("0123456789", "۰۱۲۳۴۵۶۷۸۹"))
    return f"سرور {digits} — {checked_ip}"


'''
    validate_end = '''def validate_port(value: str, *, allow_zero: bool = False) -> int:
    try:
        port = int(str(value).strip())
    except (ValueError, TypeError) as exc:
        raise ValueError("پورت باید عددی بین ۱ تا ۶۵۵۳۵ باشد.") from exc
    minimum = 0 if allow_zero else 1
    if not minimum <= port <= 65535:
        raise ValueError("پورت باید عددی بین ۱ تا ۶۵۵۳۵ باشد.")
    return port


'''
    replace_once(core, validate_end, validate_end + core_insert, "def sort_discovered_servers(servers: list[dict])")

    replace_once(ui,
        '    validate_port, write_host_config, write_settings, launch_executable,\n',
        '    validate_port, write_host_config, write_settings, launch_executable,\n    sort_discovered_servers, server_list_label,\n',
        'sort_discovered_servers, server_list_label')
    replace_once(ui,
        'self.card(row,"✦","ساخت سرور","بازی را اجرا کن؛ برنامه خودش سرور را معرفی و آماده‌بودنش را بررسی می‌کند.",self.host,True)',
        'self.card(row,"✦","ساخت سرور","نقشه و حالت بازی را انتخاب کن؛ بعد دکمهٔ شروع را بزن.",self.host_setup,True)',
        'self.host_setup,True')

    host_setup = '''    def host_setup(self):
        """Let the host choose this match's settings before launching CS2."""
        self.clear()
        tk.Label(self.main,text="تنظیم سرور",bg=BG,fg=TEXT,font=("Segoe UI",22,"bold"),anchor="e").pack(fill="x",pady=(3,5))
        tk.Label(self.main,text="نام، نقشه و حالت بازی را انتخاب کن؛ تا «شروع سرور» را نزنی، بازی اجرا نمی‌شود.",bg=BG,fg=MUTED,font=("Segoe UI",10),anchor="e",justify="right").pack(fill="x",pady=(0,14))
        panel=tk.Frame(self.main,bg=PANEL,highlightthickness=1,highlightbackground=BORDER,padx=20,pady=18)
        panel.pack(fill="x",pady=(0,12))
        name_var=tk.StringVar(value=self.name.get().strip() or "GameNet 98 LAN")
        map_var=tk.StringVar(value=self.map.get() if self.map.get() in MAPS else "de_dust2")
        mode_var=tk.StringVar(value=self.mode.get() if self.mode.get() in MODES else "Casual")
        grid=tk.Frame(panel,bg=PANEL); grid.pack(fill="x")
        grid.columnconfigure(0,weight=1,uniform="hostfields"); grid.columnconfigure(1,weight=1,uniform="hostfields")
        self.field(grid,"نام سرور",name_var,0,1)
        self.combo(grid,"نقشه",map_var,MAPS,0,0)
        self.combo(grid,"حالت بازی",mode_var,list(MODES),1,1)
        host_info=tk.Frame(grid,bg=PANEL); host_info.grid(row=1,column=0,sticky="nsew",padx=4,pady=5)
        tk.Label(host_info,text="آدرس سرور",bg=PANEL,fg=MUTED,font=("Segoe UI",9),anchor="e").pack(fill="x")
        tk.Label(host_info,text=f"{self.host_ip.get()}:{self.port.get()}",bg=PANEL,fg=ACCENT,font=("Consolas",11,"bold"),anchor="e").pack(fill="x",pady=(8,0))
        tk.Label(self.main,text="هر کامپیوتر می‌تواند سرور خودش را داشته باشد. سرورها با IP جدا هستند و انتخاب یک سرور، اتصال را به همان میزبان می‌فرستد.",bg=BG,fg=MUTED,font=("Segoe UI",9),anchor="e",justify="right",wraplength=760).pack(fill="x",pady=(2,8))
        buttons=tk.Frame(self.main,bg=BG); buttons.pack(fill="x",pady=(4,0))
        self.btn(buttons,"شروع سرور",lambda:self.start_host_from_setup(name_var,map_var,mode_var),True).pack(side="right")
        self.btn(buttons,"بازگشت",self.home,False,True).pack(side="left")

    def start_host_from_setup(self,name_var,map_var,mode_var):
        selected_map=map_var.get()
        selected_mode=mode_var.get()
        if selected_map not in MAPS or selected_mode not in MODES:
            messagebox.showerror(APP_NAME,"نقشه یا حالت بازی معتبر نیست.",parent=self.root)
            return
        self.name.set(name_var.get().strip() or "GameNet 98 LAN")
        self.map.set(selected_map)
        self.mode.set(selected_mode)
        self.save()
        self.host()

'''
    replace_once(ui, '    def advertisement(self,peer):\n', host_setup + '    def advertisement(self,peer):\n', 'def host_setup(self):')

    old_finder = '''    def finder_result(self,rows,error):
        self.searching=False; self.servers=sorted(rows,key=lambda d:(str(d.get("name","")),str(d.get("ip",""))))
        if not self.server_list or not self.server_list.winfo_exists():return
        self.server_list.delete(0,"end")
        for d in self.servers:
            state=d.get("state","starting"); label="آماده" if state=="ready" else "در حال آماده‌سازی" if state=="starting" else "آمادگی تأیید نشد"
            self.server_list.insert("end",f"{d.get('name','CS2 LAN')}   •   {d['ip']}:{d['port']}   •   {d.get('map','?')}   •   {label}")
        msg=error or (f"{len(rows)} سرور پیدا شد." if rows else "سروری پیدا نشد؛ میزبان باید همین برنامه را باز کرده باشد.")
        self.server_status.configure(text=msg,fg=GREEN if rows else MUTED); self.status.set(msg)
        if self.finder and self.finder.winfo_exists() and not self.closing:self.root.after(3500,self.auto_search)
'''
    new_finder = '''    def finder_result(self,rows,error):
        self.searching=False
        selected_key=None
        if self.server_list and self.server_list.winfo_exists() and self.server_list.curselection():
            old_index=self.server_list.curselection()[0]
            if 0 <= old_index < len(self.servers):
                old=self.servers[old_index]
                selected_key=(old.get("ip"),int_or(old.get("port"),DEFAULT_GAME_PORT))
        self.servers=sort_discovered_servers(rows)
        if not self.server_list or not self.server_list.winfo_exists():return
        self.server_list.delete(0,"end")
        selected_index=None
        for index,d in enumerate(self.servers,1):
            self.server_list.insert("end",server_list_label(index,d["ip"]))
            if selected_key==(d.get("ip"),int_or(d.get("port"),DEFAULT_GAME_PORT)):
                selected_index=index-1
        if selected_index is not None:
            self.server_list.selection_set(selected_index)
            self.server_list.activate(selected_index)
        msg=error or (f"{len(rows)} سرور پیدا شد؛ سرور موردنظر را انتخاب کن." if rows else "سروری پیدا نشد؛ میزبان باید همین برنامه را باز کرده باشد.")
        if self.server_status and self.server_status.winfo_exists():
            self.server_status.configure(text=msg,fg=GREEN if rows else MUTED)
        self.status.set(msg)
        if self.finder and self.finder.winfo_exists() and not self.closing:self.root.after(3500,self.auto_search)
'''
    replace_once(ui, old_finder, new_finder, 'self.servers=sort_discovered_servers(rows)')

    replace_once(src / "GameNet98-CS2-LAN-Offline.iss", '#define MyAppVersion "0.4.0"', '#define MyAppVersion "0.5.0"', '#define MyAppVersion "0.5.0"||#define MyAppVersion "0.5.1"')

    fa = src / "README_FA.md"
    replace_once(fa, 'نسخهٔ آزمایشی ۰.۴.۰ — رابط ساده با دو کار اصلی: «ساخت سرور» و «یافتن سرور».', 'نسخهٔ آزمایشی ۰.۵.۰ — رابط ساده با دو کار اصلی: «ساخت سرور» و «یافتن سرور».', 'نسخهٔ آزمایشی ۰.۵.۰||نسخهٔ آزمایشی ۰.۵.۱')
    replace_once(fa, '۵. کارت شبکه‌ای را که به سوئیچ گیم‌نت وصل است انتخاب کن، نام سرور و نقشه را تنظیم کن و تنظیمات را ذخیره کن.', '۵. در تنظیمات یک‌باره، مسیر بازی و کارت شبکه را انتخاب و ذخیره کن. نام، نقشه و حالت هر مسابقه را هنگام زدن «ساخت سرور» جداگانه انتخاب می‌کنی.', 'نام، نقشه و حالت هر مسابقه را')
    replace_once(fa, '- **میزبان:** «ساخت سرور» را می‌زند. برنامه فایل تنظیمات میزبان را می‌سازد، بازی را اجرا می‌کند و به‌صورت خودکار پاسخ سرور را بررسی می‌کند.', '- **میزبان:** «ساخت سرور» را می‌زند، نام، نقشه، حالت بازی را انتخاب می‌کند و سپس «شروع سرور» را می‌زند. تازه بعد از این دکمه، برنامه فایل تنظیمات میزبان را می‌سازد، بازی را اجرا می‌کند و به‌صورت خودکار پاسخ سرور را بررسی می‌کند.', '«شروع سرور» را می‌زند')
    replace_once(fa, '- **بازیکن:** «یافتن سرور» را می‌زند، میزبان را انتخاب می‌کند و «اتصال» را می‌زند.', '- **بازیکن:** «یافتن سرور» را می‌زند. فهرست فقط شمارهٔ سرور و IP آن را نشان می‌دهد؛ با انتخاب سرور و زدن «اتصال»، برنامه به همان IP و پورت اعلام‌شده وصل می‌شود. چند کامپیوتر می‌توانند هم‌زمان سرورهای مستقل داشته باشند؛ فهرست با IP مرتب می‌شود و انتخاب سرور در جست‌وجوی خودکار حفظ می‌شود.', 'فهرست فقط شمارهٔ سرور و IP آن را')

    readme = src / "README.md"
    replace_once(readme, 'Choose the LAN adapter, host name, map and mode; save the settings.\n5. Host presses **Create Server**. Clients press **Find Server**, select the host and press **Connect**.', 'Choose and save the LAN adapter once. The host presses **Create Server**, chooses server name/map/mode, then presses **Start Server**. Clients press **Find Server**, select a simple Server N — IP row and press **Connect**. Hosts are sorted numerically by IP and each selected endpoint uses the port advertised by that host.', 'Choose and save the LAN adapter once')

    replace_once(tests, '    copy_game_install, game_install_root, source_server_ready,\n', '    copy_game_install, game_install_root, source_server_ready,\n    sort_discovered_servers, server_list_label,\n', 'sort_discovered_servers, server_list_label')
    new_tests = '''    def test_discovered_servers_sort_numerically_by_ip_and_port(self):
        rows = [
            {"ip": "192.168.0.108", "port": 27015},
            {"ip": "192.168.0.9", "port": 27015},
            {"ip": "192.168.0.108", "port": 27016},
        ]
        ordered = sort_discovered_servers(rows)
        self.assertEqual([(x["ip"], x["port"]) for x in ordered], [
            ("192.168.0.9", 27015),
            ("192.168.0.108", 27015),
            ("192.168.0.108", 27016),
        ])

    def test_server_picker_label_shows_ordinal_and_only_ip(self):
        label = server_list_label(2, "192.168.0.108")
        self.assertEqual(label, "سرور ۲ — 192.168.0.108")
        self.assertNotIn("27015", label)
        self.assertNotIn("de_dust2", label)

'''
    replace_once(tests, '    def test_argument_builders(self):\n', new_tests + '    def test_argument_builders(self):\n', 'def test_server_picker_label_shows_ordinal_and_only_ip')


    # v0.5.1: make the server-picker window's close button actually close it.
    close_old = '    def close_finder(self):self.finder=None; self.server_list=None; self.server_status=None\n'
    close_new = '''    def close_finder(self):
        """Close the server picker without terminating the main app."""
        window = self.finder
        self.finder = None
        self.server_list = None
        self.server_status = None
        if window is not None:
            try:
                if window.winfo_exists():
                    window.destroy()
            except tk.TclError:
                pass
'''
    replace_once(ui, close_old, close_new, "if window.winfo_exists():")

    # Version bump and an executable regression test for the finder close action.
    replace_once(core, 'APP_VERSION = "0.5.0-beta"', 'APP_VERSION = "0.5.1-beta"', 'APP_VERSION = "0.5.1-beta"')
    replace_once(iss_path, '#define MyAppVersion "0.5.0"', '#define MyAppVersion "0.5.1"', '#define MyAppVersion "0.5.1"')
    replace_once(fa, 'نسخهٔ آزمایشی ۰.۵.۰ — رابط ساده با دو کار اصلی: «ساخت سرور» و «یافتن سرور».', 'نسخهٔ آزمایشی ۰.۵.۱ — اصلاح بسته‌شدن پنجرهٔ یافتن سرور.', 'نسخهٔ آزمایشی ۰.۵.۱')

    ui_test = src / "tests" / "test_ui.py"
    ui_test.write_text('''import importlib.machinery
import importlib.util
from pathlib import Path
import unittest

APP_PATH = Path(__file__).resolve().parents[1] / "gamenet98_lan.pyw"
LOADER = importlib.machinery.SourceFileLoader("gamenet98_lan_ui", str(APP_PATH))
SPEC = importlib.util.spec_from_loader(LOADER.name, LOADER)
UI = importlib.util.module_from_spec(SPEC)
LOADER.exec_module(UI)


class FakeWindow:
    def __init__(self, exists=True):
        self.exists = exists
        self.destroyed = False

    def winfo_exists(self):
        return self.exists

    def destroy(self):
        self.destroyed = True
        self.exists = False


class FinderCloseTests(unittest.TestCase):
    def test_close_finder_destroys_picker_and_clears_references(self):
        app = UI.App.__new__(UI.App)
        window = FakeWindow()
        app.finder = window
        app.server_list = object()
        app.server_status = object()

        app.close_finder()

        self.assertTrue(window.destroyed)
        self.assertIsNone(app.finder)
        self.assertIsNone(app.server_list)
        self.assertIsNone(app.server_status)

    def test_close_finder_tolerates_already_closed_window(self):
        app = UI.App.__new__(UI.App)
        app.finder = FakeWindow(exists=False)
        app.server_list = object()
        app.server_status = object()

        app.close_finder()

        self.assertIsNone(app.finder)
        self.assertIsNone(app.server_list)
        self.assertIsNone(app.server_status)


if __name__ == "__main__":
    unittest.main()
''', encoding="utf-8")


def repack_and_split(src: Path, repo_root: Path) -> None:
    zip_path = repo_root / "tools" / "GameNet98-CS2-LAN-source-v051.zip"
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for file in sorted(src.rglob("*")):
            if not file.is_file() or "__pycache__" in file.parts or file.suffix == ".pyc":
                continue
            arcname = Path("GameNet98-CS2-LAN") / file.relative_to(src)
            zf.write(file, arcname.as_posix())
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
    print(f"Updated v0.5.1 source ZIP: {zip_path.stat().st_size} bytes; base64 {len(encoded)} chars.")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", required=True, type=Path)
    parser.add_argument("--repo-root", required=True, type=Path)
    args = parser.parse_args()
    if not (args.source_dir / "gamenet98_lan.pyw").is_file():
        raise SystemExit(f"Expected source directory not found: {args.source_dir}")
    patch_source(args.source_dir)
    repack_and_split(args.source_dir, args.repo_root)
    print("GameNet 98 CS2 LAN source is at v0.5.1.")


if __name__ == "__main__":
    main()

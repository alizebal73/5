"""Read-only discovery of CS2 servers on the local IPv4 LAN.

This only performs Source A2S_INFO UDP queries. It does not modify or launch
any game process and intentionally never attempts to bypass Steam auth.
"""
from __future__ import annotations

import ipaddress
import os
import socket
import struct
import threading
import time
from concurrent.futures import ThreadPoolExecutor, as_completed

from . import config as _config

_A2S_INFO = b"\xff\xff\xff\xffTSource Engine Query\x00"
_PORTS = tuple(range(27015, 27021))
_TIMEOUT = 0.22
_MAX_WORKERS = 128
_CACHE_TTL = 5.0
_cache_lock = threading.Lock()
_cache_at = 0.0
_cache_result: list[dict] = []


def _network() -> ipaddress.IPv4Network | None:
    """Use explicit CIDR if configured; otherwise scan the active /24 LAN."""
    configured = os.environ.get("GAMENET98_LAN_CIDR", "").strip()
    try:
        if configured:
            net = ipaddress.ip_network(configured, strict=False)
            if not isinstance(net, ipaddress.IPv4Network) or net.num_addresses > 1024:
                return None
            return net
        ip = _config._lan_ip(force_refresh=True)
        addr = ipaddress.ip_address(ip)
        if not isinstance(addr, ipaddress.IPv4Address) or addr.is_loopback or addr.is_unspecified:
            return None
        net = ipaddress.ip_network(f"{addr}/24", strict=False)
        return net if net.num_addresses <= 1024 else None
    except (ValueError, OSError):
        return None


def _cstring(packet: bytes, offset: int) -> tuple[str, int]:
    end = packet.find(b"\x00", offset)
    if end < 0:
        raise ValueError("unterminated A2S string")
    return packet[offset:end].decode("utf-8", errors="replace"), end + 1


def _parse_info(packet: bytes) -> dict | None:
    if len(packet) < 6 or packet[:4] != b"\xff\xff\xff\xff" or packet[4] != 0x49:
        return None
    try:
        offset = 6  # header, S2A_INFO, protocol byte
        name, offset = _cstring(packet, offset)
        map_name, offset = _cstring(packet, offset)
        _folder, offset = _cstring(packet, offset)
        _game, offset = _cstring(packet, offset)
        if offset + 6 > len(packet):
            return None
        app_id = struct.unpack_from("<H", packet, offset)[0]
        offset += 2
        players, max_players, _bots = packet[offset:offset + 3]
        return {
            "name": name or "CS2 LAN Server",
            "map": map_name,
            "players": int(players),
            "max_players": int(max_players),
            "app_id": int(app_id),
        }
    except (ValueError, IndexError, struct.error):
        return None


def _query_one(ip: str, port: int) -> dict | None:
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.settimeout(_TIMEOUT)
            sock.sendto(_A2S_INFO, (ip, port))
            response, _ = sock.recvfrom(4096)
            # Source servers may return an A2S challenge; echo it in the query.
            if (len(response) >= 9 and response[:4] == b"\xff\xff\xff\xff"
                    and response[4] == 0x41):
                sock.sendto(_A2S_INFO + response[5:9], (ip, port))
                response, _ = sock.recvfrom(4096)
            info = _parse_info(response)
            if not info or info.get("app_id") not in (0, 730):
                return None
            info.update({"ip": ip, "port": int(port), "address": f"{ip}:{port}"})
            return info
    except (OSError, TimeoutError):
        return None


def scan_servers(client_ip: str | None = None, force: bool = False) -> list[dict]:
    """Discover reachable CS2 A2S servers on the local network only."""
    global _cache_at, _cache_result
    network = _network()
    if network is None:
        return []
    if client_ip not in ("127.0.0.1", "::1"):
        try:
            client = ipaddress.ip_address(client_ip or "")
            if not isinstance(client, ipaddress.IPv4Address) or client not in network:
                return []
        except ValueError:
            return []
    now = time.monotonic()
    with _cache_lock:
        if not force and now - _cache_at < _CACHE_TTL:
            return [dict(x) for x in _cache_result]

    hosts = [str(ip) for ip in network.hosts()]
    found: dict[str, dict] = {}
    with ThreadPoolExecutor(max_workers=_MAX_WORKERS) as pool:
        futures = [pool.submit(_query_one, ip, port) for ip in hosts for port in _PORTS]
        for future in as_completed(futures):
            try:
                item = future.result()
                if item:
                    found[item["address"]] = item
            except Exception:
                continue
    result = sorted(found.values(), key=lambda x: (x["ip"], x["port"]))
    with _cache_lock:
        _cache_at = time.monotonic()
        _cache_result = [dict(x) for x in result]
    return result

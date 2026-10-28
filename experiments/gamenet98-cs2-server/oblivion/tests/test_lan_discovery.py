import ipaddress
import struct

from cs2servergui.lan_discovery import _parse_info, _network


def test_parse_a2s_info_packet():
    packet = (
        b"\xff\xff\xff\xff\x49\x11"
        + b"GameNet 98 Dust2\x00"
        + b"de_dust2\x00"
        + b"csgo\x00"
        + b"Counter-Strike 2\x00"
        + struct.pack("<H", 730)
        + bytes([3, 10, 0, ord("d"), ord("w"), 0, 1])
        + b"1.0\x00"
    )
    info = _parse_info(packet)
    assert info is not None
    assert info["name"] == "GameNet 98 Dust2"
    assert info["map"] == "de_dust2"
    assert info["players"] == 3
    assert info["max_players"] == 10
    assert info["app_id"] == 730


def test_parse_rejects_non_a2s_info():
    assert _parse_info(b"\xff\xff\xff\xff\x41\x00\x00\x00\x00") is None


def test_parse_rejects_wrong_app_id_packet_shape():
    packet = (
        b"\xff\xff\xff\xff\x49\x11"
        + b"Example\x00map\x00folder\x00game\x00"
        + struct.pack("<H", 10)
        + bytes([1, 10, 0, ord("d"), ord("w"), 0, 1])
        + b"1.0\x00"
    )
    info = _parse_info(packet)
    assert info is not None and info["app_id"] == 10


def test_network_derived_from_lan_ip(monkeypatch):
    monkeypatch.setenv("GAMENET98_LAN_CIDR", "192.168.0.0/24")
    net = _network()
    assert net == ipaddress.ip_network("192.168.0.0/24")

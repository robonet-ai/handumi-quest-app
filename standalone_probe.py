#!/usr/bin/env python3
"""Small independent client for the recovered YubiQuestApp v0.1.0 protocol."""

from __future__ import annotations

import argparse
import json
import socket
import struct
import time
from typing import Any


def read_pose(ip: str, port: int) -> dict[str, Any]:
    with socket.create_connection((ip, port), timeout=5.0) as sock:
        stream = sock.makefile("r", encoding="utf-8", errors="replace")
        while True:
            line = stream.readline()
            if not line:
                raise ConnectionError("Quest closed the TCP pose stream")
            line = line.strip()
            if line:
                return json.loads(line)


def sync_once(ip: str, port: int) -> tuple[int, int, int]:
    t1 = time.monotonic_ns()
    request = struct.pack("<BQ", 1, t1)
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.settimeout(2.0)
        sock.sendto(request, (ip, port))
        data, _ = sock.recvfrom(32)
    t4 = time.monotonic_ns()
    packet_id, t1_echo, quest_ns = struct.unpack("<BQQ", data)
    if packet_id != 2 or t1_echo != t1:
        raise ValueError("Unexpected time-sync response")
    rtt_ns = t4 - t1
    offset_ns = quest_ns - ((t1 + t4) // 2)
    return quest_ns, rtt_ns, offset_ns


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("quest_ip")
    parser.add_argument("--tcp-port", type=int, default=65432)
    parser.add_argument("--sync-port", type=int, default=42000)
    args = parser.parse_args()

    quest_ns, rtt_ns, offset_ns = sync_once(args.quest_ip, args.sync_port)
    print(f"sync: quest={quest_ns} rtt_ms={rtt_ns / 1e6:.3f} offset_ms={offset_ns / 1e6:.3f}")
    print(json.dumps(read_pose(args.quest_ip, args.tcp_port), indent=2))


if __name__ == "__main__":
    main()

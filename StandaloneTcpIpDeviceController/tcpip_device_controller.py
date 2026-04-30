from __future__ import annotations

import argparse
import queue
import socket
import sys
import threading
import time
import tkinter as tk
from dataclasses import dataclass
from tkinter import filedialog, messagebox, ttk


TERMINATORS = {
    "LF": "\n",
    "CR": "\r",
    "CRLF": "\r\n",
    "None": "",
}

DEFAULT_HOST = "10.128.1.176"
DEFAULT_PORT = 51123


@dataclass
class TcpSettings:
    host: str
    port: int
    timeout_s: float = 2.0
    terminator: str = "LF"
    encoding: str = "ascii"

    @property
    def terminator_text(self) -> str:
        return TERMINATORS.get(self.terminator, "\n")


class TcpIpDeviceClient:
    def __init__(self, settings: TcpSettings) -> None:
        self.settings = settings
        self._socket: socket.socket | None = None
        self._lock = threading.Lock()

    @property
    def is_connected(self) -> bool:
        return self._socket is not None

    def connect(self) -> None:
        with self._lock:
            self.disconnect()
            sock = socket.create_connection(
                (self.settings.host, self.settings.port),
                timeout=self.settings.timeout_s,
            )
            sock.settimeout(self.settings.timeout_s)
            self._socket = sock

    def disconnect(self) -> None:
        sock = self._socket
        self._socket = None
        if sock is not None:
            try:
                sock.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            sock.close()

    def send(self, command: str) -> None:
        payload = self._format_command(command)
        with self._lock:
            sock = self._require_socket()
            sock.sendall(payload)

    def query(self, command: str) -> str:
        payload = self._format_command(command)
        with self._lock:
            sock = self._require_socket()
            sock.sendall(payload)
            return self._read_response(sock)

    def read_available(self) -> str:
        with self._lock:
            sock = self._require_socket()
            return self._read_response(sock)

    def _format_command(self, command: str) -> bytes:
        text = command.rstrip("\r\n") + self.settings.terminator_text
        return text.encode(self.settings.encoding, errors="replace")

    def _read_response(self, sock: socket.socket) -> str:
        chunks: list[bytes] = []
        terminator = self.settings.terminator_text.encode(self.settings.encoding, errors="replace")
        start = time.monotonic()

        while True:
            if time.monotonic() - start > self.settings.timeout_s:
                break
            try:
                chunk = sock.recv(4096)
            except socket.timeout:
                break
            if not chunk:
                break

            chunks.append(chunk)
            if terminator and b"".join(chunks).endswith(terminator):
                break
            if not terminator:
                sock.settimeout(0.05)
                try:
                    continue
                finally:
                    sock.settimeout(self.settings.timeout_s)

        return b"".join(chunks).decode(self.settings.encoding, errors="replace").rstrip("\r\n")

    def _require_socket(self) -> socket.socket:
        if self._socket is None:
            raise RuntimeError("Not connected.")
        return self._socket


class TcpIpDeviceApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("TCP/IP Device Controller")
        self.geometry("980x680")
        self.minsize(760, 520)

        self.client: TcpIpDeviceClient | None = None
        self.log_queue: queue.Queue[str] = queue.Queue()

        self.host_var = tk.StringVar(value=DEFAULT_HOST)
        self.port_var = tk.StringVar(value=str(DEFAULT_PORT))
        self.timeout_var = tk.StringVar(value="2.0")
        self.terminator_var = tk.StringVar(value="LF")
        self.command_var = tk.StringVar(value="*IDN?")
        self.status_var = tk.StringVar(value="Disconnected")

        self._build_ui()
        self.after(100, self._drain_log_queue)
        self.protocol("WM_DELETE_WINDOW", self._on_close)

    def _build_ui(self) -> None:
        self.columnconfigure(0, weight=1)
        self.rowconfigure(2, weight=1)

        connection = ttk.LabelFrame(self, text="Connection")
        connection.grid(row=0, column=0, sticky="ew", padx=12, pady=(12, 6))
        for col in (1, 3, 5, 7):
            connection.columnconfigure(col, weight=1)

        ttk.Label(connection, text="Host").grid(row=0, column=0, padx=8, pady=8, sticky="w")
        ttk.Entry(connection, textvariable=self.host_var, width=18).grid(row=0, column=1, padx=4, pady=8, sticky="ew")
        ttk.Label(connection, text="Port").grid(row=0, column=2, padx=8, pady=8, sticky="w")
        ttk.Entry(connection, textvariable=self.port_var, width=8).grid(row=0, column=3, padx=4, pady=8, sticky="ew")
        ttk.Label(connection, text="Timeout").grid(row=0, column=4, padx=8, pady=8, sticky="w")
        ttk.Entry(connection, textvariable=self.timeout_var, width=8).grid(row=0, column=5, padx=4, pady=8, sticky="ew")
        ttk.Label(connection, text="Terminator").grid(row=0, column=6, padx=8, pady=8, sticky="w")
        ttk.Combobox(
            connection,
            textvariable=self.terminator_var,
            values=list(TERMINATORS),
            width=8,
            state="readonly",
        ).grid(row=0, column=7, padx=4, pady=8, sticky="ew")

        ttk.Button(connection, text="Connect", command=self.connect_device).grid(row=0, column=8, padx=(12, 4), pady=8)
        ttk.Button(connection, text="Disconnect", command=self.disconnect_device).grid(row=0, column=9, padx=4, pady=8)
        ttk.Label(connection, textvariable=self.status_var).grid(row=1, column=0, columnspan=10, padx=8, pady=(0, 8), sticky="w")

        command_frame = ttk.LabelFrame(self, text="Command")
        command_frame.grid(row=1, column=0, sticky="ew", padx=12, pady=6)
        command_frame.columnconfigure(0, weight=1)
        command_entry = ttk.Entry(command_frame, textvariable=self.command_var)
        command_entry.grid(row=0, column=0, columnspan=5, sticky="ew", padx=8, pady=8)
        command_entry.bind("<Return>", lambda _event: self.query_command())

        ttk.Button(command_frame, text="Send", command=self.send_command).grid(row=1, column=0, padx=8, pady=(0, 8), sticky="ew")
        ttk.Button(command_frame, text="Query", command=self.query_command).grid(row=1, column=1, padx=4, pady=(0, 8), sticky="ew")
        ttk.Button(command_frame, text="Read", command=self.read_response).grid(row=1, column=2, padx=4, pady=(0, 8), sticky="ew")
        ttk.Button(command_frame, text="Run Script", command=self.run_script_file).grid(row=1, column=3, padx=4, pady=(0, 8), sticky="ew")
        ttk.Button(command_frame, text="Clear Log", command=self.clear_log).grid(row=1, column=4, padx=8, pady=(0, 8), sticky="ew")

        log_frame = ttk.LabelFrame(self, text="Log")
        log_frame.grid(row=2, column=0, sticky="nsew", padx=12, pady=(6, 12))
        log_frame.rowconfigure(0, weight=1)
        log_frame.columnconfigure(0, weight=1)

        self.log_text = tk.Text(log_frame, wrap="word", font=("Consolas", 10))
        self.log_text.grid(row=0, column=0, sticky="nsew")
        scroll = ttk.Scrollbar(log_frame, command=self.log_text.yview)
        scroll.grid(row=0, column=1, sticky="ns")
        self.log_text.configure(yscrollcommand=scroll.set)

    def connect_device(self) -> None:
        def work() -> None:
            try:
                settings = self._settings_from_ui()
                client = TcpIpDeviceClient(settings)
                client.connect()
                self.client = client
                self._log(f"Connected to {settings.host}:{settings.port}")
                self.status_var.set(f"Connected to {settings.host}:{settings.port}")
            except Exception as exc:
                self._log(f"Connect failed: {exc}")
                self.status_var.set("Disconnected")

        threading.Thread(target=work, daemon=True).start()

    def disconnect_device(self) -> None:
        if self.client is not None:
            self.client.disconnect()
        self.status_var.set("Disconnected")
        self._log("Disconnected.")

    def send_command(self) -> None:
        command = self.command_var.get()
        self._run_client_action(lambda client: client.send(command), f"> {command}")

    def query_command(self) -> None:
        command = self.command_var.get()

        def action(client: TcpIpDeviceClient) -> None:
            response = client.query(command)
            self._log(f"> {command}")
            self._log(f"< {response if response else '[no response]'}")

        self._run_client_action(action)

    def read_response(self) -> None:
        def action(client: TcpIpDeviceClient) -> None:
            response = client.read_available()
            self._log(f"< {response if response else '[no response]'}")

        self._run_client_action(action)

    def run_script_file(self) -> None:
        path = filedialog.askopenfilename(
            title="Select command script",
            filetypes=[("Text files", "*.txt *.cmd *.scpi"), ("All files", "*.*")],
        )
        if not path:
            return

        def action(client: TcpIpDeviceClient) -> None:
            with open(path, "r", encoding="utf-8") as handle:
                for raw_line in handle:
                    command = raw_line.strip()
                    if not command or command.startswith("#"):
                        continue
                    if command.endswith("?"):
                        response = client.query(command)
                        self._log(f"> {command}")
                        self._log(f"< {response if response else '[no response]'}")
                    else:
                        client.send(command)
                        self._log(f"> {command}")

        self._run_client_action(action, f"Running script: {path}")

    def clear_log(self) -> None:
        self.log_text.delete("1.0", tk.END)

    def _run_client_action(self, action, prelog: str | None = None) -> None:
        def work() -> None:
            try:
                client = self.client
                if client is None or not client.is_connected:
                    raise RuntimeError("Connect to a device first.")
                if prelog:
                    self._log(prelog)
                action(client)
            except Exception as exc:
                self._log(f"Error: {exc}")

        threading.Thread(target=work, daemon=True).start()

    def _settings_from_ui(self) -> TcpSettings:
        return TcpSettings(
            host=self.host_var.get().strip(),
            port=int(self.port_var.get().strip()),
            timeout_s=float(self.timeout_var.get().strip()),
            terminator=self.terminator_var.get().strip(),
        )

    def _log(self, message: str) -> None:
        timestamp = time.strftime("%H:%M:%S")
        self.log_queue.put(f"[{timestamp}] {message}")

    def _drain_log_queue(self) -> None:
        while True:
            try:
                line = self.log_queue.get_nowait()
            except queue.Empty:
                break
            self.log_text.insert(tk.END, line + "\n")
            self.log_text.see(tk.END)
        self.after(100, self._drain_log_queue)

    def _on_close(self) -> None:
        try:
            if self.client is not None:
                self.client.disconnect()
        finally:
            self.destroy()


def run_cli(args: argparse.Namespace) -> int:
    settings = TcpSettings(
        host=args.host,
        port=args.port,
        timeout_s=args.timeout,
        terminator=args.terminator,
        encoding=args.encoding,
    )
    client = TcpIpDeviceClient(settings)
    try:
        client.connect()
        if args.query is not None:
            print(client.query(args.query))
        elif args.command is not None:
            client.send(args.command)
        elif args.interactive:
            print(f"Connected to {settings.host}:{settings.port}. Empty line exits.")
            while True:
                command = input("> ").strip()
                if not command:
                    break
                if command.endswith("?"):
                    print(client.query(command))
                else:
                    client.send(command)
        else:
            print(f"Connected to {settings.host}:{settings.port}")
        return 0
    finally:
        client.disconnect()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Standalone TCP/IP device controller.")
    parser.add_argument("--host", help="Device host/IP address. If omitted, launches the GUI.")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="Device TCP port.")
    parser.add_argument("--timeout", type=float, default=2.0, help="Socket timeout in seconds.")
    parser.add_argument("--terminator", choices=list(TERMINATORS), default="LF", help="Command terminator.")
    parser.add_argument("--encoding", default="ascii", help="Command/response text encoding.")
    parser.add_argument("--command", help="Send one command without reading a response.")
    parser.add_argument("--query", help="Send one command and print the response.")
    parser.add_argument("--interactive", action="store_true", help="Open an interactive CLI session.")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if args.host:
        return run_cli(args)

    try:
        app = TcpIpDeviceApp()
        app.mainloop()
        return 0
    except tk.TclError as exc:
        messagebox.showerror("TCP/IP Device Controller", str(exc))
        return 1


if __name__ == "__main__":
    sys.exit(main())

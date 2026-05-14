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
DEFAULT_TIMEOUT_S = 5.0
DEFAULT_TERMINATOR = "CRLF"

OPO_STATUS_READBACKS = [
    ("state", "State", "state?", ""),
    ("interlock", "Interlock", "interlock?", ""),
    ("temperature", "Temperature", "temperature?", "C"),
    ("humidity", "Humidity", "humidity?", "%"),
    ("pump_power", "Pump power", "pump_power?", "mW/raw"),
    ("signal_power", "Signal power", "signal_power?", "mW/raw"),
    ("signal_wavelength", "Signal wavelength", "signal_wavelength?", "nm"),
    ("idler_wavelength", "Idler wavelength", "idler_wavelength?", "nm"),
    ("bandwidth", "Bandwidth", "bandwidth?", "nm"),
    ("xtal_position", "Crystal position", "xtal_position?", "steps"),
    ("cavity_position", "Cavity position", "cavity_position?", "steps"),
    ("lyot_position", "Lyot position", "lyot_position?", "steps"),
]


@dataclass
class TcpSettings:
    host: str
    port: int
    timeout_s: float = DEFAULT_TIMEOUT_S
    terminator: str = DEFAULT_TERMINATOR
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
        self.geometry("1180x780")
        self.minsize(900, 620)

        self.client: TcpIpDeviceClient | None = None
        self.log_queue: queue.Queue[str] = queue.Queue()
        self.status_fields = {
            key: tk.StringVar(value="-") for key, _label, _command, _unit in OPO_STATUS_READBACKS
        }
        self.poll_interval_var = tk.StringVar(value="2.0")
        self.polling_enabled = False
        self.polling_busy = False
        self.workflow_stop_event = threading.Event()

        self.host_var = tk.StringVar(value=DEFAULT_HOST)
        self.port_var = tk.StringVar(value=str(DEFAULT_PORT))
        self.timeout_var = tk.StringVar(value=str(DEFAULT_TIMEOUT_S))
        self.terminator_var = tk.StringVar(value=DEFAULT_TERMINATOR)
        self.command_var = tk.StringVar(value="state?")
        self.status_var = tk.StringVar(value="Disconnected")
        self.signal_wavelength_var = tk.StringVar(value="1500")
        self.idler_wavelength_var = tk.StringVar(value="3000")
        self.pump_wavelength_var = tk.StringVar(value="1031.2")
        self.repetition_rate_var = tk.StringVar(value="80000000")
        self.xtal_position_var = tk.StringVar(value="10000")
        self.xtal_step1_var = tk.StringVar(value="100")
        self.xtal_step2_var = tk.StringVar(value="10")
        self.cavity_position_var = tk.StringVar(value="1000000")
        self.cavity_fullsteps_var = tk.StringVar(value="10")
        self.cavity_substeps_var = tk.StringVar(value="1")
        self.lyot_position_var = tk.StringVar(value="5000")
        self.lyot_step1_var = tk.StringVar(value="10")
        self.lyot_step2_var = tk.StringVar(value="10")
        self.stabilize_lambda_tol_var = tk.StringVar(value="0.5")
        self.stabilize_power_tol_var = tk.StringVar(value="5")
        self.log_interval_var = tk.StringVar(value="10")

        self._build_ui()
        self.after(100, self._drain_log_queue)
        self.protocol("WM_DELETE_WINDOW", self._on_close)

    def _build_ui(self) -> None:
        self.columnconfigure(0, weight=1)
        self.rowconfigure(3, weight=1)

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
        ttk.Button(connection, text="Probe Terminators", command=self.probe_terminators).grid(row=1, column=8, columnspan=2, padx=(12, 4), pady=(0, 8), sticky="ew")
        ttk.Label(connection, textvariable=self.status_var).grid(row=1, column=0, columnspan=8, padx=8, pady=(0, 8), sticky="w")

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

        self._build_opo_controls()

        log_frame = ttk.LabelFrame(self, text="Log")
        log_frame.grid(row=3, column=0, sticky="nsew", padx=12, pady=(6, 12))
        log_frame.rowconfigure(0, weight=1)
        log_frame.columnconfigure(0, weight=1)

        self.log_text = tk.Text(log_frame, wrap="word", font=("Consolas", 10))
        self.log_text.grid(row=0, column=0, sticky="nsew")
        scroll = ttk.Scrollbar(log_frame, command=self.log_text.yview)
        scroll.grid(row=0, column=1, sticky="ns")
        self.log_text.configure(yscrollcommand=scroll.set)

    def _build_opo_controls(self) -> None:
        opo_frame = ttk.LabelFrame(self, text="Levante IR OPO Controls")
        opo_frame.grid(row=2, column=0, sticky="ew", padx=12, pady=6)
        opo_frame.columnconfigure(0, weight=1)

        notebook = ttk.Notebook(opo_frame)
        notebook.grid(row=0, column=0, sticky="ew", padx=8, pady=8)

        status_tab = ttk.Frame(notebook)
        tuning_tab = ttk.Frame(notebook)
        actuator_tab = ttk.Frame(notebook)
        routine_tab = ttk.Frame(notebook)
        shutter_tab = ttk.Frame(notebook)

        notebook.add(status_tab, text="Status")
        notebook.add(tuning_tab, text="Tuning")
        notebook.add(actuator_tab, text="Actuators")
        notebook.add(routine_tab, text="Routines")
        notebook.add(shutter_tab, text="Shutters/Log")

        self._build_status_tab(status_tab)
        self._build_tuning_tab(tuning_tab)
        self._build_actuator_tab(actuator_tab)
        self._build_routine_tab(routine_tab)
        self._build_shutter_tab(shutter_tab)

    def _build_status_tab(self, parent: ttk.Frame) -> None:
        dashboard = ttk.Frame(parent)
        dashboard.grid(row=0, column=0, columnspan=6, sticky="ew", padx=4, pady=4)
        for index, (key, label, command, unit) in enumerate(OPO_STATUS_READBACKS):
            row = index // 4
            col = (index % 4) * 4
            ttk.Label(dashboard, text=label).grid(row=row, column=col, padx=(6, 3), pady=4, sticky="w")
            ttk.Label(dashboard, textvariable=self.status_fields[key], width=14, anchor="e").grid(
                row=row,
                column=col + 1,
                padx=3,
                pady=4,
                sticky="ew",
            )
            ttk.Label(dashboard, text=unit).grid(row=row, column=col + 2, padx=(0, 4), pady=4, sticky="w")
            ttk.Button(dashboard, text="?", width=3, command=lambda c=command: self.query_literal(c)).grid(
                row=row,
                column=col + 3,
                padx=(3, 6),
                pady=4,
            )
        for col in range(16):
            dashboard.columnconfigure(col, weight=1 if col % 4 == 1 else 0)

        ttk.Separator(parent, orient="horizontal").grid(row=1, column=0, columnspan=6, sticky="ew", padx=4, pady=6)
        ttk.Button(parent, text="Refresh Status", command=self.query_all_status).grid(
            row=2,
            column=0,
            padx=4,
            pady=5,
            sticky="ew",
        )
        ttk.Label(parent, text="Poll interval (s)").grid(row=2, column=1, padx=4, pady=5, sticky="e")
        ttk.Entry(parent, textvariable=self.poll_interval_var, width=8).grid(row=2, column=2, padx=4, pady=5, sticky="w")
        ttk.Button(parent, text="Start Poll", command=self.start_status_polling).grid(
            row=2,
            column=3,
            padx=4,
            pady=5,
            sticky="ew",
        )
        ttk.Button(parent, text="Stop Poll", command=self.stop_status_polling).grid(
            row=2,
            column=4,
            padx=4,
            pady=5,
            sticky="ew",
        )
        ttk.Button(parent, text="Stop OPO Routines", command=self.stop_all_routines).grid(
            row=2,
            column=5,
            padx=4,
            pady=5,
            sticky="ew",
        )
        quick_queries = [
            ("State", "state?"),
            ("Interlock", "interlock?"),
            ("Busy motors", "xtal_busy?;cavity_busy?;lyot_busy?"),
            ("Spectrum", "spectrum?"),
        ]
        for index, (label, command) in enumerate(quick_queries):
            ttk.Button(parent, text=label, command=lambda c=command: self.query_multi_literal(c)).grid(
                row=3,
                column=index,
                padx=4,
                pady=5,
                sticky="ew",
            )
        for col in range(6):
            parent.columnconfigure(col, weight=1)

    def _build_tuning_tab(self, parent: ttk.Frame) -> None:
        self._add_set_query_row(parent, 0, "Signal wavelength (nm)", "set_signal_wavelength", self.signal_wavelength_var)
        self._add_set_query_row(parent, 1, "Idler wavelength (nm)", "set_idler_wavelength", self.idler_wavelength_var)
        self._add_set_query_row(parent, 2, "Pump wavelength (nm)", "pump_wavelength", self.pump_wavelength_var)
        self._add_set_query_row(parent, 3, "Repetition rate (Hz)", "repetitionrate", self.repetition_rate_var)
        ttk.Button(parent, text="Tune Signal Workflow", command=self.tune_signal_workflow).grid(
            row=4,
            column=0,
            columnspan=2,
            padx=6,
            pady=(8, 4),
            sticky="ew",
        )
        ttk.Button(parent, text="Stop Workflow", command=self.stop_workflow).grid(
            row=4,
            column=2,
            columnspan=2,
            padx=4,
            pady=(8, 4),
            sticky="ew",
        )

        toggles = ttk.LabelFrame(parent, text="Modes")
        toggles.grid(row=0, column=4, rowspan=5, sticky="nsew", padx=(14, 4), pady=4)
        for row, command in enumerate(("automatic", "optimization", "fast_tuning")):
            ttk.Label(toggles, text=command).grid(row=row, column=0, padx=6, pady=4, sticky="w")
            ttk.Button(toggles, text="On", command=lambda c=command: self.send_bool(c, True)).grid(row=row, column=1, padx=3, pady=4)
            ttk.Button(toggles, text="Off", command=lambda c=command: self.send_bool(c, False)).grid(row=row, column=2, padx=3, pady=4)
            ttk.Button(toggles, text="?", command=lambda c=command: self.query_literal(f"{c}?")).grid(row=row, column=3, padx=3, pady=4)

        parent.columnconfigure(1, weight=1)
        parent.columnconfigure(4, weight=1)

    def _build_actuator_tab(self, parent: ttk.Frame) -> None:
        xtal = ttk.LabelFrame(parent, text="Crystal")
        cavity = ttk.LabelFrame(parent, text="Cavity")
        lyot = ttk.LabelFrame(parent, text="Lyot")
        xtal.grid(row=0, column=0, sticky="nsew", padx=4, pady=4)
        cavity.grid(row=0, column=1, sticky="nsew", padx=4, pady=4)
        lyot.grid(row=0, column=2, sticky="nsew", padx=4, pady=4)
        for col in range(3):
            parent.columnconfigure(col, weight=1)

        self._add_position_controls(
            xtal,
            "set_xtal",
            self.xtal_position_var,
            ("step_xtal_dec1", "step_xtal_dec2", "step_xtal_inc1", "step_xtal_inc2"),
            ("xtal_position?", "xtal_busy?", "set_xtal?", "set_xtal_lambda", "correct_xtal_offset"),
        )
        self._add_set_query_row(xtal, 4, "Step 1", "set_xtal_steplength1", self.xtal_step1_var)
        self._add_set_query_row(xtal, 5, "Step 2", "set_xtal_steplength2", self.xtal_step2_var)

        self._add_position_controls(
            cavity,
            "set_cavity",
            self.cavity_position_var,
            ("full_dec_cavity", "sub_dec_cavity", "sub_inc_cavity", "full_inc_cavity"),
            ("cavity_position?", "cavity_busy?", "set_cavity?", "set_cavity_lambda", "correct_cavity_offset"),
        )
        self._add_set_query_row(cavity, 4, "Full steps", "set_cavity_fullsteps", self.cavity_fullsteps_var)
        self._add_set_query_row(cavity, 5, "Sub steps", "set_cavity_substeps", self.cavity_substeps_var)

        self._add_position_controls(
            lyot,
            "set_lyot",
            self.lyot_position_var,
            ("step_lyot_dec1", "step_lyot_dec2", "step_lyot_inc1", "step_lyot_inc2"),
            ("lyot_position?", "lyot_busy?", "set_lyot?"),
        )
        self._add_set_query_row(lyot, 4, "Step 1", "set_lyot_steplength1", self.lyot_step1_var)
        self._add_set_query_row(lyot, 5, "Step 2", "set_lyot_steplength2", self.lyot_step2_var)

    def _build_routine_tab(self, parent: ttk.Frame) -> None:
        commands = [
            "search_signal",
            "search_signal_lyot",
            "tune_lyot",
            "max_power_xtal",
            "max_peak_xtal",
            "max_power_cavity",
            "max_peak_cavity",
            "stabilize_cavity",
            "stabilize_power_active",
        ]
        for row, command in enumerate(commands):
            ttk.Label(parent, text=command).grid(row=row, column=0, padx=6, pady=3, sticky="w")
            ttk.Button(parent, text="Start/On", command=lambda c=command: self.send_bool(c, True)).grid(row=row, column=1, padx=3, pady=3)
            ttk.Button(parent, text="Stop/Off", command=lambda c=command: self.send_bool(c, False)).grid(row=row, column=2, padx=3, pady=3)
            ttk.Button(parent, text="Query", command=lambda c=command: self.query_literal(f"{c}?")).grid(row=row, column=3, padx=3, pady=3)

        self._add_set_query_row(parent, 0, "Lambda tolerance (nm)", "stabilize_tolerance_lambda", self.stabilize_lambda_tol_var, start_col=4)
        self._add_set_query_row(parent, 1, "Power tolerance (%)", "stabilize_tolerance_power", self.stabilize_power_tol_var, start_col=4)
        ttk.Button(parent, text="Stabilize active?", command=lambda: self.query_literal("stabilize_active?")).grid(
            row=2,
            column=4,
            columnspan=4,
            padx=6,
            pady=3,
            sticky="ew",
        )
        ttk.Button(parent, text="Stop All Routines", command=self.stop_all_routines).grid(
            row=3,
            column=4,
            columnspan=4,
            padx=6,
            pady=3,
            sticky="ew",
        )
        for col in (0, 4):
            parent.columnconfigure(col, weight=1)

    def _build_shutter_tab(self, parent: ttk.Frame) -> None:
        shutters = [
            ("Pump shutter", "shutter_open"),
            ("Signal shutter", "signal_open"),
            ("Idler shutter", "idler_open"),
        ]
        for row, (label, command) in enumerate(shutters):
            ttk.Label(parent, text=label).grid(row=row, column=0, padx=6, pady=5, sticky="w")
            ttk.Button(parent, text="Open", command=lambda c=command: self.send_bool(c, True, confirm=True)).grid(row=row, column=1, padx=4, pady=5)
            ttk.Button(parent, text="Close", command=lambda c=command: self.send_bool(c, False)).grid(row=row, column=2, padx=4, pady=5)
            ttk.Button(parent, text="Query", command=lambda c=command: self.query_literal(f"{c}?")).grid(row=row, column=3, padx=4, pady=5)
        ttk.Button(parent, text="Close All Shutters", command=self.close_all_shutters).grid(
            row=0,
            column=4,
            rowspan=3,
            padx=8,
            pady=5,
            sticky="nsew",
        )

        ttk.Separator(parent, orient="horizontal").grid(row=3, column=0, columnspan=6, sticky="ew", padx=6, pady=8)
        ttk.Button(parent, text="Save Parameters Now", command=lambda: self.send_literal("log_parameters_user")).grid(
            row=4,
            column=0,
            columnspan=2,
            padx=6,
            pady=4,
            sticky="ew",
        )
        ttk.Label(parent, text="Log interval (s)").grid(row=4, column=2, padx=6, pady=4, sticky="e")
        ttk.Entry(parent, textvariable=self.log_interval_var, width=10).grid(row=4, column=3, padx=4, pady=4, sticky="ew")
        ttk.Button(parent, text="Set", command=lambda: self.send_assignment("log_interval", self.log_interval_var)).grid(row=4, column=4, padx=4, pady=4)
        ttk.Button(parent, text="?", command=lambda: self.query_literal("log_interval?")).grid(row=4, column=5, padx=4, pady=4)

        for row, command in enumerate(("log_parameters_tuning", "log_parameters_timed"), start=5):
            ttk.Label(parent, text=command).grid(row=row, column=0, padx=6, pady=4, sticky="w")
            ttk.Button(parent, text="On", command=lambda c=command: self.send_bool(c, True)).grid(row=row, column=1, padx=4, pady=4)
            ttk.Button(parent, text="Off", command=lambda c=command: self.send_bool(c, False)).grid(row=row, column=2, padx=4, pady=4)
            ttk.Button(parent, text="?", command=lambda c=command: self.query_literal(f"{c}?")).grid(row=row, column=3, padx=4, pady=4)

        parent.columnconfigure(3, weight=1)

    def _add_set_query_row(
        self,
        parent: ttk.Frame,
        row: int,
        label: str,
        command: str,
        variable: tk.StringVar,
        start_col: int = 0,
    ) -> None:
        ttk.Label(parent, text=label).grid(row=row, column=start_col, padx=6, pady=4, sticky="w")
        ttk.Entry(parent, textvariable=variable, width=14).grid(row=row, column=start_col + 1, padx=4, pady=4, sticky="ew")
        ttk.Button(parent, text="Set", command=lambda: self.send_assignment(command, variable)).grid(row=row, column=start_col + 2, padx=3, pady=4)
        ttk.Button(parent, text="?", command=lambda: self.query_literal(f"{command}?")).grid(row=row, column=start_col + 3, padx=3, pady=4)

    def _add_position_controls(
        self,
        parent: ttk.Frame,
        set_command: str,
        variable: tk.StringVar,
        step_commands: tuple[str, str, str, str],
        query_commands: tuple[str, ...],
    ) -> None:
        self._add_set_query_row(parent, 0, "Position", set_command, variable)
        labels = ("--", "-", "+", "++")
        for col, (label, command) in enumerate(zip(labels, step_commands, strict=False)):
            ttk.Button(parent, text=label, command=lambda c=command: self.send_literal(c)).grid(row=1, column=col, padx=3, pady=4, sticky="ew")
        for idx, command in enumerate(query_commands):
            ttk.Button(parent, text=command, command=lambda c=command: self.query_or_send(c)).grid(
                row=2 + idx // 2,
                column=(idx % 2) * 2,
                columnspan=2,
                padx=3,
                pady=3,
                sticky="ew",
            )
        parent.columnconfigure(1, weight=1)

    def connect_device(self) -> None:
        def work() -> None:
            try:
                settings = self._settings_from_ui()
                client = TcpIpDeviceClient(settings)
                client.connect()
                self.client = client
                self._log(f"Connected to {settings.host}:{settings.port}")
                self.after(0, self.status_var.set, f"Connected to {settings.host}:{settings.port}")
                self.after(0, self.query_all_status)
            except Exception as exc:
                self._log(f"Connect failed: {exc}")
                self.after(0, self.status_var.set, "Disconnected")

        threading.Thread(target=work, daemon=True).start()

    def disconnect_device(self) -> None:
        self.polling_enabled = False
        self.workflow_stop_event.set()
        if self.client is not None:
            self.client.disconnect()
        self.status_var.set("Disconnected")
        self._log("Disconnected.")

    def probe_terminators(self) -> None:
        def work() -> None:
            original_terminator = self.terminator_var.get()
            self._log("Probing line terminators with state? ...")
            for terminator in ("CRLF", "CR", "LF", "None"):
                try:
                    settings = self._settings_from_ui()
                    settings.terminator = terminator
                    client = TcpIpDeviceClient(settings)
                    client.connect()
                    response = client.query("state?")
                    client.disconnect()
                    shown = response if response else "[no response]"
                    self._log(f"{terminator}: {shown}")
                    if response:
                        self.terminator_var.set(terminator)
                        self._log(f"Using terminator: {terminator}")
                        return
                except Exception as exc:
                    self._log(f"{terminator}: error: {exc}")
            self.terminator_var.set(original_terminator)
            self._log("No terminator produced a response.")

        threading.Thread(target=work, daemon=True).start()

    def send_literal(self, command: str) -> None:
        self.command_var.set(command)
        self._run_client_action(lambda client: client.send(command), f"> {command}")

    def query_literal(self, command: str) -> None:
        self.command_var.set(command)

        def action(client: TcpIpDeviceClient) -> None:
            response = client.query(command)
            self._record_status_response(command, response)
            self._log(f"> {command}")
            self._log(f"< {response if response else '[no response]'}")

        self._run_client_action(action)

    def query_or_send(self, command: str) -> None:
        if command.endswith("?"):
            self.query_literal(command)
        else:
            self.send_literal(command)

    def query_multi_literal(self, commands_text: str) -> None:
        commands = [command.strip() for command in commands_text.split(";") if command.strip()]
        if not commands:
            return
        self.command_var.set(commands[0])

        def action(client: TcpIpDeviceClient) -> None:
            for command in commands:
                if command.endswith("?"):
                    response = client.query(command)
                    self._record_status_response(command, response)
                    self._log(f"> {command}")
                    self._log(f"< {response if response else '[no response]'}")
                else:
                    client.send(command)
                    self._log(f"> {command}")

        self._run_client_action(action)

    def send_assignment(self, command: str, variable: tk.StringVar) -> None:
        value = variable.get().strip()
        if not value:
            messagebox.showwarning("Missing Value", f"Enter a value for {command}.")
            return
        self.send_literal(f"{command}={value}")

    def send_bool(self, command: str, state: bool, confirm: bool = False) -> None:
        value = "TRUE" if state else "FALSE"
        full_command = f"{command}={value}"
        if confirm and state:
            ok = messagebox.askyesno(
                "Confirm OPO Shutter Open",
                f"Send {full_command}?\n\nOnly continue if the optical path is safe.",
            )
            if not ok:
                return
        self.send_literal(full_command)

    def query_all_status(self) -> None:
        def action(client: TcpIpDeviceClient) -> None:
            self._query_status_values(client, log_each=True)

        self._run_client_action(action, "Querying OPO status...")

    def start_status_polling(self) -> None:
        if self.polling_enabled:
            return
        self.polling_enabled = True
        self._log("Started status polling.")
        self._poll_status_once()

    def stop_status_polling(self) -> None:
        self.polling_enabled = False
        self._log("Stopped status polling.")

    def _poll_status_once(self) -> None:
        if not self.polling_enabled:
            return
        if self.polling_busy:
            self.after(self._poll_interval_ms(), self._poll_status_once)
            return
        self.polling_busy = True

        def work() -> None:
            try:
                client = self.client
                if client is None or not client.is_connected:
                    raise RuntimeError("Connect to a device first.")
                self._query_status_values(client, log_each=False)
            except Exception as exc:
                self._log(f"Polling stopped: {exc}")
                self.polling_enabled = False
            finally:
                self.polling_busy = False
                if self.polling_enabled:
                    self.after(self._poll_interval_ms(), self._poll_status_once)

        threading.Thread(target=work, daemon=True).start()

    def _poll_interval_ms(self) -> int:
        try:
            seconds = max(0.25, float(self.poll_interval_var.get().strip()))
        except ValueError:
            seconds = 2.0
        return int(seconds * 1000)

    def _query_status_values(self, client: TcpIpDeviceClient, log_each: bool) -> dict[str, str]:
        values: dict[str, str] = {}
        for key, _label, command, _unit in OPO_STATUS_READBACKS:
            response = client.query(command)
            values[key] = response
            self._record_status_response(command, response)
            if log_each:
                self._log(f"> {command}")
                self._log(f"< {response if response else '[no response]'}")
        return values

    def _record_status_response(self, command: str, response: str) -> None:
        for key, _label, status_command, _unit in OPO_STATUS_READBACKS:
            if command == status_command:
                text = response if response else "-"
                self.after(0, self.status_fields[key].set, text)
                return

    def stop_all_routines(self) -> None:
        commands = [
            "automatic=FALSE",
            "search_signal=FALSE",
            "search_signal_lyot=FALSE",
            "tune_lyot=FALSE",
            "max_power_xtal=FALSE",
            "max_peak_xtal=FALSE",
            "max_power_cavity=FALSE",
            "max_peak_cavity=FALSE",
            "stabilize_cavity=FALSE",
            "stabilize_power_active=FALSE",
        ]

        def action(client: TcpIpDeviceClient) -> None:
            for command in commands:
                client.send(command)
                self._log(f"> {command}")
            self._log("Routine stop commands sent.")

        self.workflow_stop_event.set()
        self._run_client_action(action, "Stopping OPO routines...")

    def close_all_shutters(self) -> None:
        commands = ["signal_open=FALSE", "idler_open=FALSE", "shutter_open=FALSE"]

        def action(client: TcpIpDeviceClient) -> None:
            for command in commands:
                client.send(command)
                self._log(f"> {command}")
            self._log("Close-shutter commands sent.")

        self._run_client_action(action, "Closing all shutters...")

    def tune_signal_workflow(self) -> None:
        target_text = self.signal_wavelength_var.get().strip()
        try:
            target_nm = float(target_text)
        except ValueError:
            messagebox.showwarning("Invalid Wavelength", "Enter a numeric signal wavelength in nm.")
            return
        ok = messagebox.askyesno(
            "Confirm OPO Tuning",
            f"Set signal wavelength to {target_nm:g} nm and enable automatic tuning?",
        )
        if not ok:
            return
        self.workflow_stop_event.clear()

        def work() -> None:
            try:
                client = self.client
                if client is None or not client.is_connected:
                    raise RuntimeError("Connect to a device first.")
                commands = [f"set_signal_wavelength={target_nm:g}", "automatic=TRUE"]
                self._log(f"Starting signal tune workflow for {target_nm:g} nm.")
                for command in commands:
                    if self.workflow_stop_event.is_set():
                        break
                    client.send(command)
                    self._log(f"> {command}")
                    time.sleep(0.2)
                deadline = time.monotonic() + 120.0
                while time.monotonic() < deadline and not self.workflow_stop_event.is_set():
                    values = self._query_status_values(client, log_each=False)
                    state = values.get("state", "").strip().lower()
                    wavelength = values.get("signal_wavelength", "").strip()
                    self._log(
                        "Tune watch: "
                        f"state={state or '-'}, signal_wavelength={wavelength or '-'}, "
                        f"signal_power={values.get('signal_power', '-') or '-'}"
                    )
                    try:
                        wavelength_nm = float(wavelength)
                    except ValueError:
                        wavelength_nm = None
                    if state == "idle" and wavelength_nm is not None and abs(wavelength_nm - target_nm) <= 0.2:
                        self._log("Signal tune workflow reached target.")
                        return
                    time.sleep(2.0)
                if self.workflow_stop_event.is_set():
                    self._log("Signal tune workflow stopped by user.")
                else:
                    self._log("Signal tune workflow timed out after 120 s.")
            except Exception as exc:
                self._log(f"Workflow error: {exc}")

        threading.Thread(target=work, daemon=True).start()

    def stop_workflow(self) -> None:
        self.workflow_stop_event.set()
        self.stop_all_routines()

    def send_command(self) -> None:
        command = self.command_var.get()
        self._run_client_action(lambda client: client.send(command), f"> {command}")

    def query_command(self) -> None:
        command = self.command_var.get()

        def action(client: TcpIpDeviceClient) -> None:
            response = client.query(command)
            self._record_status_response(command, response)
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
                        self._record_status_response(command, response)
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
        self.polling_enabled = False
        self.workflow_stop_event.set()
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
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT_S, help="Socket timeout in seconds.")
    parser.add_argument("--terminator", choices=list(TERMINATORS), default=DEFAULT_TERMINATOR, help="Command terminator.")
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

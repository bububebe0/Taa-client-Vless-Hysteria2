#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
Taa Client — графический клиент для работы с прокси-серверами VLESS и Hysteria2.
Основные возможности:
- Добавление серверов из буфера обмена (vless:// или hysteria2:// ссылки).
- Управление несколькими серверами, выбор активного, установка сервера по умолчанию.
- Проверка задержки (TCP ping) до сервера.
- Подключение/отключение прокси с автоматической настройкой системного прокси Windows.
- Гибкая маршрутизация (split tunneling): список доменов и IP-адресов, которые направляются через прокси.
- Поддержка нескольких списков маршрутизации (создание, переименование, удаление).
- Настройки: автозапуск, выбор языка (русский/английский), DNS (системный, DoH, DoT) с опцией направления через прокси.
- Сворачивание в системный трей с меню для быстрого переключения серверов и управления.
- Все данные (серверы, настройки, списки маршрутов, логи) хранятся в папке рядом с исполняемым файлом (портативный режим).

Используемые технологии:
- customtkinter — современный GUI на основе tkinter.
- pystray — иконка в системном трее.
- dnspython / requests — тестирование DNS.
- subprocess — запуск и остановка sing-box (ядро прокси).
- winreg — управление системным прокси Windows.
- pyinstaller — для сборки в один .exe.

Версия: 1.1
Автор: Bububebe0
"""

import customtkinter as ctk
import tkinter as tk
from tkinter import filedialog, simpledialog, messagebox
import subprocess
import json
import os
import sys
import urllib.parse
import ctypes
import winreg
import ipaddress
import threading
import socket
import time
import pystray
from PIL import Image
import dns.resolver
import dns.name

def resource_path(relative_path):
    try:
        base_path = sys._MEIPASS
    except Exception:
        base_path = os.path.abspath(".")
    return os.path.join(base_path, relative_path)

def data_path(relative_path=""):
    if getattr(sys, 'frozen', False):
        base = os.path.dirname(sys.executable)
    else:
        base = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(base, relative_path)

ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")

APP_DATA_DIR = data_path()
DATA_DIR = data_path("data")
DB_FILE = data_path("data/servers.json")
SETTINGS_FILE = data_path("data/settings.json")
CONFIG_FILE = data_path("data/config.json")
ROUTES_DIR = data_path("list")
LOG_FILE = data_path("proxy.log")

BG_COLOR = "#09090B"
SIDEBAR_COLOR = "#18181B"
CARD_COLOR = "#18181B"
BORDER_COLOR = "#27272A"
ACCENT_COLOR = "#4F46E5"
ACCENT_HOVER = "#4338CA"
SUCCESS_COLOR = "#10B981"
DANGER_COLOR = "#EF4444"
DANGER_HOVER = "#DC2626"
TEXT_MAIN = "#F8FAFC"
TEXT_MUTED = "#A1A1AA"
ACTIVE_ITEM_COLOR = "#27272A"

TRANSLATIONS = {
    "ru": {
        "title": "Taa Client | Vless Hysteria2 [1.1]",
        "app_name": "Сервера",
        "add_from_clipboard": "➕ Из буфера",
        "import_configs": "Импорт конфигов",
        "btn_exit": "Выйти из приложения",
        "settings": "Настройки",
        "connection_info": "Информация о соединении",
        "name": "Название:",
        "address": "Адрес:",
        "hide_ip": "Скрыть IP",
        "check_ping": "📡 Пинг",
        "set_default": "⭐ По умолчанию",
        "default_marker": " (дефолт)",
        "btn_delete": "Удалить",
        "routing": "Маршрутизация",
        "split_tunneling": "Сплит-туннелирование",
        "status_disconnected": "Отключено",
        "status_connected": "Подключено",
        "status_error": "❌ Ошибка",
        "btn_connect": "Подключиться",
        "btn_disconnect": "Отключиться",
        "ping_checking": "Проверка...",
        "server_not_selected": "Сервер не выбран",
        "settings_title": "Настройки",
        "autostart": "Автозапуск при старте Windows",
        "language_label": "Язык интерфейса:",
        "view_logs": "Посмотреть логи",
        "restart_app": "Перезагрузить",
        "import_title": "Импорт конфигураций",
        "import_file": "Импорт сайтов из файла",
        "import_clipboard": "VLESS/Hysteria2 из буфера",
        "select_file": "Выберите файл со списком сайтов",
        "log_not_found": "Файл логов пока не создан.",
        "tray_open": "Развернуть окно",
        "tray_exit": "Выйти",
        "new_routes_file": "Новый список",
        "delete_routes_file": "Удалить",
        "rename_routes_file": "Переименовать",
        "enter_name": "Введите имя",
        "confirm_delete": "Удалить список",
        "confirm_delete_text": "Вы уверены, что хотите удалить список '{0}'?",
        "error": "Ошибка",
        "cannot_delete_last": "Нельзя удалить единственный список маршрутизации.",
        "dns_settings": "Настройки DNS",
        "dns_type": "Тип DNS:",
        "dns_system": "Системный DNS",
        "dns_doh": "DNS over HTTPS (DoH)",
        "dns_dot": "DNS over TLS (DoT)",
        "dns_server_address": "Адрес сервера:",
        "dns_through_proxy": "Направлять DNS через прокси",
        "dns_test": "Проверить DNS",
        "dns_test_success": "DNS работает",
        "dns_test_fail": "DNS не отвечает",
        "dns_invalid_address": "Некорректный адрес DNS",
        "dns_apply_restart": "Изменения DNS вступят в силу после перезапуска прокси."
    },
    "en": {
        "title": "Minimal Proxy Client [1.2]",
        "app_name": "Servers",
        "add_from_clipboard": "➕ From Clipboard",
        "import_configs": "Import Configs",
        "btn_exit": "Quit Application",
        "settings": "Settings",
        "connection_info": "Connection Info",
        "name": "Name:",
        "address": "Address:",
        "hide_ip": "Hide IP",
        "check_ping": "📡 Ping",
        "set_default": "⭐ Set Default",
        "default_marker": " (default)",
        "btn_delete": "Delete",
        "routing": "Routing",
        "split_tunneling": "Split Tunneling",
        "status_disconnected": "Disconnected",
        "status_connected": "Connected",
        "status_error": "❌ Error",
        "btn_connect": "Connect",
        "btn_disconnect": "Disconnect",
        "ping_checking": "Checking...",
        "server_not_selected": "No server selected",
        "settings_title": "Settings",
        "autostart": "Launch on Windows startup",
        "language_label": "Interface Language:",
        "view_logs": "View Logs",
        "restart_app": "Restart Application",
        "import_title": "Import Configurations",
        "import_file": "Import sites from file",
        "import_clipboard": "VLESS/Hysteria2 from clipboard",
        "select_file": "Select file with site list",
        "log_not_found": "Log file not created yet.",
        "tray_open": "Open Window",
        "tray_exit": "Quit",
        "new_routes_file": "New list",
        "delete_routes_file": "Delete",
        "rename_routes_file": "Rename",
        "enter_name": "Enter name",
        "confirm_delete": "Delete list",
        "confirm_delete_text": "Are you sure you want to delete list '{0}'?",
        "error": "Error",
        "cannot_delete_last": "Cannot delete the only routing list.",
        "dns_settings": "DNS Settings",
        "dns_type": "DNS Type:",
        "dns_system": "System DNS",
        "dns_doh": "DNS over HTTPS (DoH)",
        "dns_dot": "DNS over TLS (DoT)",
        "dns_server_address": "Server Address:",
        "dns_through_proxy": "Route DNS through proxy",
        "dns_test": "Test DNS",
        "dns_test_success": "DNS works",
        "dns_test_fail": "DNS not responding",
        "dns_invalid_address": "Invalid DNS address",
        "dns_apply_restart": "DNS changes will take effect after proxy restart."
    }
}

class ProxyApp(ctk.CTk):
    def __init__(self):
        super().__init__()

        os.makedirs(ROUTES_DIR, exist_ok=True)
        os.makedirs(DATA_DIR, exist_ok=True)

        self.app_settings = self.load_app_settings()
        self.lang = self.app_settings.get("language", "ru")

        self.title(self.tr("title"))

        try:
            self.iconbitmap(resource_path("ico.ico"))
        except Exception as e:
            print(f"Не удалось загрузить иконку окна: {e}")

        try:
            self.icon_on = Image.open(resource_path("ico.ico"))
            self.icon_off = Image.open(resource_path("off.ico"))
        except Exception:
            self.icon_on = Image.new('RGB', (64, 64), color=(79, 70, 229))
            self.icon_off = Image.new('RGB', (64, 64), color=(100, 100, 100))

        self.geometry("950x700")
        saved_geometry = self.app_settings.get("window_geometry")
        saved_state = self.app_settings.get("window_state", "normal")
        if saved_geometry:
            self.geometry(saved_geometry)
        else:
            self.center_window(self, 950, 700)
        if saved_state == "zoomed":
            self.state("zoomed")

        self.minsize(860, 620)
        self.configure(fg_color=BG_COLOR)

        self.proxy_process = None
        self.servers = []
        self.server_buttons = []
        self.selected_server_index = -1

        self.hide_host_var = ctk.BooleanVar(value=True)
        self.split_tunnel_var = ctk.BooleanVar(value=self.app_settings.get("split_tunneling", True))
        self.autostart_var = ctk.BooleanVar(value=self.check_autostart())

        self.font_title = ctk.CTkFont(family="Segoe UI", size=24, weight="bold")
        self.font_main = ctk.CTkFont(family="Segoe UI", size=14)
        self.font_bold = ctk.CTkFont(family="Segoe UI", size=14, weight="bold")
        self.font_small = ctk.CTkFont(family="Segoe UI", size=12)

        self.current_routes_file = "routes.txt"
        self.routes_list = []

        self.sidebar_frame = ctk.CTkFrame(self, width=280, corner_radius=0, fg_color=SIDEBAR_COLOR)
        self.sidebar_frame.pack(side="left", fill="y")
        self.sidebar_frame.pack_propagate(False)

        self.logo_label = ctk.CTkLabel(self.sidebar_frame, text=self.tr("app_name"), font=self.font_title, text_color=TEXT_MAIN)
        self.logo_label.pack(pady=(35, 25), padx=25, anchor="w")

        self.add_btn = ctk.CTkButton(
            self.sidebar_frame, text=self.tr("add_from_clipboard"), font=self.font_bold,
            fg_color=ACCENT_COLOR, hover_color=ACCENT_HOVER, corner_radius=10, height=45,
            command=self.add_from_clipboard
        )
        self.add_btn.pack(pady=(0, 15), padx=20, fill="x")

        self.server_list_frame = ctk.CTkScrollableFrame(self.sidebar_frame, fg_color="transparent")
        self.server_list_frame.pack(pady=5, padx=10, fill="both", expand=True)

        self.btn_quit = ctk.CTkButton(
            self.sidebar_frame, text=self.tr("btn_exit"), font=self.font_main,
            fg_color="transparent", hover_color=CARD_COLOR, border_width=1, border_color=BORDER_COLOR,
            text_color=DANGER_COLOR, corner_radius=10, height=40, command=self.cleanup_and_exit
        )
        self.btn_quit.pack(side="bottom", pady=(5, 25), padx=20, fill="x")

        self.btn_settings = ctk.CTkButton(
            self.sidebar_frame, text=self.tr("settings"), font=self.font_main,
            fg_color="transparent", hover_color=CARD_COLOR, border_width=1, border_color=BORDER_COLOR,
            text_color=TEXT_MAIN, corner_radius=10, height=40, command=self.open_settings_dialog
        )
        self.btn_settings.pack(side="bottom", pady=(5, 5), padx=20, fill="x")

        self.btn_import = ctk.CTkButton(
            self.sidebar_frame, text=self.tr("import_configs"), font=self.font_main,
            fg_color="transparent", hover_color=CARD_COLOR, border_width=1, border_color=BORDER_COLOR,
            text_color=TEXT_MAIN, corner_radius=10, height=40, command=self.open_import_dialog
        )
        self.btn_import.pack(side="bottom", pady=(15, 5), padx=20, fill="x")

        self.main_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.main_frame.pack(side="right", fill="both", expand=True, padx=35, pady=35)

        self.info_card = ctk.CTkFrame(self.main_frame, corner_radius=16, fg_color=CARD_COLOR, border_width=1, border_color=BORDER_COLOR)
        self.info_card.pack(fill="x", pady=(0, 25))

        self.name_var = ctk.StringVar(value=self.tr("server_not_selected"))
        self.host_var = ctk.StringVar(value="—")

        info_header = ctk.CTkFrame(self.info_card, fg_color="transparent")
        info_header.pack(fill="x", padx=25, pady=(20, 15))
        ctk.CTkLabel(info_header, text=self.tr("connection_info"), font=self.font_bold, text_color=TEXT_MAIN).pack(side="left")

        self.delete_btn = ctk.CTkButton(
            info_header, text=self.tr("btn_delete"), font=self.font_small,
            fg_color="transparent", text_color=DANGER_COLOR, hover_color=BORDER_COLOR,
            height=28, width=80, corner_radius=6, command=self.delete_current_server, state="disabled"
        )
        self.delete_btn.pack(side="right")

        details_frame = ctk.CTkFrame(self.info_card, fg_color="transparent")
        details_frame.pack(fill="x", padx=25, pady=(0, 20))

        name_row = ctk.CTkFrame(details_frame, fg_color="transparent")
        name_row.pack(fill="x", pady=5)
        ctk.CTkLabel(name_row, text=self.tr("name"), font=self.font_main, text_color=TEXT_MUTED, width=80, anchor="w").pack(side="left")
        ctk.CTkLabel(name_row, textvariable=self.name_var, font=self.font_bold, text_color=TEXT_MAIN).pack(side="left", fill="x", expand=True, padx=15)

        host_row = ctk.CTkFrame(details_frame, fg_color="transparent")
        host_row.pack(fill="x", pady=5)
        ctk.CTkLabel(host_row, text=self.tr("address"), font=self.font_main, text_color=TEXT_MUTED, width=80, anchor="w").pack(side="left")
        ctk.CTkLabel(host_row, textvariable=self.host_var, font=self.font_main, text_color=TEXT_MAIN).pack(side="left", padx=15)

        self.hide_switch = ctk.CTkSwitch(
            host_row, text=self.tr("hide_ip"), font=self.font_small, text_color=TEXT_MUTED,
            variable=self.hide_host_var, command=self.update_host_display,
            onvalue=True, offvalue=False, switch_width=38, switch_height=20
        )
        self.hide_switch.pack(side="right")

        ping_row = ctk.CTkFrame(details_frame, fg_color="transparent")
        ping_row.pack(fill="x", pady=(15, 5))
        self.ping_btn = ctk.CTkButton(
            ping_row, text=self.tr("check_ping"), font=self.font_main, height=36, corner_radius=8,
            fg_color="transparent", border_width=1, border_color=BORDER_COLOR,
            hover_color=SIDEBAR_COLOR, text_color=TEXT_MAIN, command=self.check_ping_thread, state="disabled"
        )
        self.ping_btn.pack(side="left")

        self.default_btn = ctk.CTkButton(
            ping_row, text=self.tr("set_default"), font=self.font_main, height=36, corner_radius=8,
            fg_color="transparent", border_width=1, border_color=BORDER_COLOR,
            hover_color=SIDEBAR_COLOR, text_color=DANGER_COLOR,
            command=self.set_current_as_default, state="disabled"
        )
        self.default_btn.pack(side="left", padx=10)

        self.ping_label = ctk.CTkLabel(ping_row, text="", font=self.font_bold)
        self.ping_label.pack(side="left", padx=10)

        self.status_connect_frame = ctk.CTkFrame(self.info_card, fg_color="transparent")
        self.status_connect_frame.pack(fill="x", padx=25, pady=(20, 20))

        self.status_label = ctk.CTkLabel(self.status_connect_frame, text=self.tr("status_disconnected"), font=self.font_title, text_color=TEXT_MUTED)
        self.status_label.pack(side="left")

        self.connect_btn = ctk.CTkButton(
            self.status_connect_frame, text=self.tr("btn_connect"), font=self.font_bold, fg_color=ACCENT_COLOR,
            hover_color=ACCENT_HOVER, height=50, width=220, corner_radius=10,
            command=self.toggle_connection, state="disabled"
        )
        self.connect_btn.pack(side="right")

        self.routing_card = ctk.CTkFrame(self.main_frame, corner_radius=16, fg_color=CARD_COLOR, border_width=1, border_color=BORDER_COLOR)
        self.routing_card.pack(fill="both", expand=True, pady=(0, 25))

        route_header = ctk.CTkFrame(self.routing_card, fg_color="transparent")
        route_header.pack(fill="x", padx=25, pady=(20, 10))
        ctk.CTkLabel(route_header, text=self.tr("routing"), font=self.font_bold, text_color=TEXT_MAIN).pack(side="left")

        self.split_switch = ctk.CTkSwitch(
            route_header, text=self.tr("split_tunneling"), font=self.font_small, text_color=TEXT_MUTED,
            variable=self.split_tunnel_var, command=self.on_split_toggle,
            onvalue=True, offvalue=False, switch_width=38, switch_height=20
        )
        self.split_switch.pack(side="right")

        routes_control_frame = ctk.CTkFrame(self.routing_card, fg_color="transparent")
        routes_control_frame.pack(fill="x", padx=25, pady=(5, 10))

        self.routes_combo = ctk.CTkComboBox(
            routes_control_frame,
            values=[],
            command=self.on_routes_file_selected,
            width=250,
            height=32,
            fg_color=SIDEBAR_COLOR,
            border_color=BORDER_COLOR,
            border_width=1,
            button_color=BORDER_COLOR,
            button_hover_color=ACTIVE_ITEM_COLOR,
            dropdown_fg_color=SIDEBAR_COLOR,
            dropdown_hover_color=ACTIVE_ITEM_COLOR,
            dropdown_text_color=TEXT_MAIN,
            corner_radius=8,
            font=self.font_main,
            dropdown_font=self.font_main,
            state="readonly"
        )
        self.routes_combo.pack(side="left", padx=(0, 10))

        self.new_routes_btn = ctk.CTkButton(
            routes_control_frame, text=self.tr("new_routes_file"), font=self.font_small,
            fg_color="transparent", border_width=1, border_color=BORDER_COLOR,
            text_color=TEXT_MAIN, hover_color=SIDEBAR_COLOR,
            width=70, height=30, corner_radius=6, command=self.create_new_routes_file
        )
        self.new_routes_btn.pack(side="left", padx=2)

        self.delete_routes_btn = ctk.CTkButton(
            routes_control_frame, text=self.tr("delete_routes_file"), font=self.font_small,
            fg_color="transparent", border_width=1, border_color=BORDER_COLOR,
            text_color=DANGER_COLOR, hover_color=SIDEBAR_COLOR,
            width=70, height=30, corner_radius=6, command=self.delete_routes_file
        )
        self.delete_routes_btn.pack(side="left", padx=2)

        self.rename_routes_btn = ctk.CTkButton(
            routes_control_frame, text=self.tr("rename_routes_file"), font=self.font_small,
            fg_color="transparent", border_width=1, border_color=BORDER_COLOR,
            text_color=TEXT_MAIN, hover_color=SIDEBAR_COLOR,
            width=90, height=30, corner_radius=6, command=self.rename_routes_file
        )
        self.rename_routes_btn.pack(side="left", padx=2)

        self.routing_textbox = ctk.CTkTextbox(
            self.routing_card, font=self.font_main, fg_color=SIDEBAR_COLOR, text_color=TEXT_MAIN,
            corner_radius=10, border_width=1, border_color=BORDER_COLOR
        )
        self.routing_textbox.pack(fill="both", expand=True, padx=25, pady=(0, 25))

        self.refresh_routes_list()
        self.load_servers_from_file()
        self.load_routes()
        self.toggle_split_state()
        self.select_default_server_on_start()

        self.create_tray_icon()
        self.protocol("WM_DELETE_WINDOW", self.hide_window)

    def refresh_routes_list(self):
        try:
            files = [f for f in os.listdir(ROUTES_DIR) if f.endswith('.txt')]
            if not files:
                default_file = "routes.txt"
                default_path = os.path.join(ROUTES_DIR, default_file)
                if not os.path.exists(default_path):
                    with open(default_path, 'w', encoding='utf-8') as f:
                        f.write("instagram.com\ntwitter.com\n2ip.ru")
                files = [default_file]
            self.routes_list = sorted(files)
            self.routes_combo.configure(values=self.routes_list)
            if self.current_routes_file not in self.routes_list:
                self.current_routes_file = self.routes_list[0]
            self.routes_combo.set(self.current_routes_file)
        except Exception as e:
            print(f"Ошибка обновления списка маршрутов: {e}")

    def on_routes_file_selected(self, choice):
        if choice != self.current_routes_file:
            self.save_current_routes()
            self.current_routes_file = choice
            self.load_routes_from_file(choice)
            self.restart_proxy_if_needed()

    def load_routes_from_file(self, filename):
        filepath = os.path.join(ROUTES_DIR, filename)
        try:
            if os.path.exists(filepath):
                with open(filepath, 'r', encoding='utf-8') as f:
                    content = f.read().strip()
                self.routing_textbox.delete("1.0", "end")
                self.routing_textbox.insert("1.0", content)
            else:
                self.routing_textbox.delete("1.0", "end")
        except Exception as e:
            print(f"Ошибка загрузки маршрутов из {filename}: {e}")

    def save_current_routes(self):
        self.save_routes_to_file(self.current_routes_file)

    def save_routes_to_file(self, filename):
        os.makedirs(ROUTES_DIR, exist_ok=True)
        filepath = os.path.join(ROUTES_DIR, filename)
        try:
            content = self.routing_textbox.get("1.0", "end-1c").strip()
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(content)
        except Exception as e:
            print(f"Ошибка сохранения маршрутов в {filename}: {e}")

    def create_new_routes_file(self):
        name = simpledialog.askstring(self.tr("new_routes_file"), self.tr("enter_name"),
                                      parent=self, initialvalue="new_list.txt")
        if not name:
            return
        if not name.endswith('.txt'):
            name += '.txt'
        if name in self.routes_list:
            messagebox.showerror(self.tr("error"), f"Файл {name} уже существует.")
            return
        filepath = os.path.join(ROUTES_DIR, name)
        try:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write("")
            self.refresh_routes_list()
            self.save_current_routes()
            self.current_routes_file = name
            self.routes_combo.set(name)
            self.routing_textbox.delete("1.0", "end")
            self.restart_proxy_if_needed()
        except Exception as e:
            messagebox.showerror(self.tr("error"), f"Не удалось создать файл: {e}")

    def delete_routes_file(self):
        if len(self.routes_list) <= 1:
            messagebox.showerror(self.tr("error"), self.tr("cannot_delete_last"))
            return
        confirm = messagebox.askyesno(self.tr("confirm_delete"),
                                      self.tr("confirm_delete_text").format(self.current_routes_file))
        if not confirm:
            return
        filepath = os.path.join(ROUTES_DIR, self.current_routes_file)
        try:
            os.remove(filepath)
            self.refresh_routes_list()
            self.current_routes_file = self.routes_list[0]
            self.routes_combo.set(self.current_routes_file)
            self.load_routes_from_file(self.current_routes_file)
            self.restart_proxy_if_needed()
        except Exception as e:
            messagebox.showerror(self.tr("error"), f"Не удалось удалить файл: {e}")

    def rename_routes_file(self):
        old_name = self.current_routes_file
        new_name = simpledialog.askstring(self.tr("rename_routes_file"), self.tr("enter_name"),
                                          parent=self, initialvalue=old_name)
        if not new_name or new_name == old_name:
            return
        if not new_name.endswith('.txt'):
            new_name += '.txt'
        if new_name in self.routes_list:
            messagebox.showerror(self.tr("error"), f"Файл {new_name} уже существует.")
            return
        old_path = os.path.join(ROUTES_DIR, old_name)
        new_path = os.path.join(ROUTES_DIR, new_name)
        try:
            os.rename(old_path, new_path)
            self.refresh_routes_list()
            self.current_routes_file = new_name
            self.routes_combo.set(new_name)
        except Exception as e:
            messagebox.showerror(self.tr("error"), f"Не удалось переименовать файл: {e}")

    def restart_proxy_if_needed(self):
        if self.proxy_process is not None and self.selected_server_index != -1:
            self.stop_proxy()
            self.toggle_connection()

    def set_current_as_default(self):
        if self.selected_server_index != -1:
            server_name = self.servers[self.selected_server_index]["name"]
            current_default = self.app_settings.get("default_server", "")
            if current_default == server_name:
                self.app_settings["default_server"] = ""
                self.default_btn.configure(text_color=DANGER_COLOR)
            else:
                self.app_settings["default_server"] = server_name
                self.default_btn.configure(text_color=SUCCESS_COLOR)
            self.save_app_settings()
            self.update_server_list()

    def set_current_as_default_from_tray(self, icon, item):
        self.after(0, self.set_current_as_default)

    def select_default_server_on_start(self):
        default_name = self.app_settings.get("default_server")
        if default_name:
            for i, server in enumerate(self.servers):
                if server["name"] == default_name:
                    self.select_server(i)
                    break

    def build_tray_menu(self):
        server_items = []
        default_name = self.app_settings.get("default_server", "")
        for i, s in enumerate(self.servers):
            def make_callback(idx):
                return lambda icon, item: self.after(0, self.select_server, idx)
            def make_checked_condition(idx):
                return lambda item: self.selected_server_index == idx
            display_name = s["name"]
            if display_name == default_name:
                display_name += self.tr("default_marker")
            server_items.append(
                pystray.MenuItem(
                    display_name[:35] + ("..." if len(display_name) > 35 else ""),
                    make_callback(i),
                    checked=make_checked_condition(i),
                    radio=True
                )
            )
        if not server_items:
            server_items.append(pystray.MenuItem("Пусто", lambda icon, item: None, enabled=False))
        def is_not_connected(item):
            return self.proxy_process is None and self.selected_server_index != -1
        def is_connected(item):
            return self.proxy_process is not None
        def get_status_text(item):
            return "Статус: Подключено" if self.proxy_process is not None else "Статус: Отключено"
        return pystray.Menu(
            pystray.MenuItem(get_status_text, lambda icon, item: None, enabled=False),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Подключиться", self.connect_from_tray, enabled=is_not_connected),
            pystray.MenuItem("Отключиться", self.disconnect_from_tray, enabled=is_connected),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem(" " + self.tr("app_name"), pystray.Menu(*server_items)),
            pystray.MenuItem(
                "⭐ " + self.tr("set_default"),
                self.set_current_as_default_from_tray,
                checked=lambda item: (self.selected_server_index != -1 and
                                     self.servers[self.selected_server_index]["name"] == self.app_settings.get("default_server", ""))
            ),
            pystray.MenuItem(
                " " + self.tr("split_tunneling"),
                self.toggle_routing_from_tray,
                checked=lambda item: self.split_tunnel_var.get()
            ),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem(self.tr("tray_open"), self.show_window, default=True),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem(self.tr("tray_exit"), self.quit_app)
        )

    def update_tray_menu(self):
        if hasattr(self, 'tray_icon') and self.tray_icon is not None:
            self.tray_icon.menu = self.build_tray_menu()
            try:
                self.tray_icon.update_menu()
            except Exception:
                pass
            self.after(200, self._delayed_tray_update)

    def _delayed_tray_update(self):
        if hasattr(self, 'tray_icon') and self.tray_icon is not None:
            try:
                self.tray_icon.update_menu()
            except Exception:
                pass

    def create_tray_icon(self):
        menu = self.build_tray_menu()
        self.tray_icon = pystray.Icon("MinimalProxyClient", self.icon_off, self.tr("title"), menu)
        threading.Thread(target=self.tray_icon.run, daemon=True).start()

    def hide_window(self):
        self.withdraw()

    def show_window(self, icon=None, item=None):
        self.after(0, self.deiconify)

    def quit_app(self, icon=None, item=None):
        self.after(0, self.cleanup_and_exit)

    def cleanup_and_exit(self):
        if hasattr(self, 'tray_icon') and self.tray_icon is not None:
            self.tray_icon.visible = False
            self.tray_icon.stop()
        self.set_system_proxy(False)
        self.save_current_routes()
        self.save_app_settings()
        if self.proxy_process:
            self.stop_proxy()
        self.quit()
        self.destroy()
        os._exit(0)

    def connect_from_tray(self, icon, item):
        self.after(0, self.toggle_connection)

    def disconnect_from_tray(self, icon, item):
        self.after(0, self.stop_proxy)

    def toggle_routing_from_tray(self, icon, item):
        self.after(0, self._toggle_routing_internal)

    def _toggle_routing_internal(self):
        current_state = self.split_tunnel_var.get()
        self.split_tunnel_var.set(not current_state)
        self.toggle_split_state()
        self.save_app_settings()
        self.restart_proxy_if_needed()

    def tr(self, key):
        return TRANSLATIONS.get(self.lang, TRANSLATIONS["ru"]).get(key, key)

    def load_app_settings(self):
        default_settings = {
            "split_tunneling": True,
            "language": "ru",
            "default_server": "",
            "window_geometry": None,
            "window_state": "normal",
            "dns_type": "system",
            "dns_server": "https://1.1.1.1/dns-query",
            "dns_through_proxy": True
        }
        if os.path.exists(SETTINGS_FILE):
            try:
                with open(SETTINGS_FILE, "r", encoding="utf-8") as f:
                    loaded = json.load(f)
                    default_settings.update(loaded)
            except Exception:
                pass
        return default_settings

    def save_app_settings(self):
        self.app_settings["split_tunneling"] = self.split_tunnel_var.get()
        try:
            geometry = self.geometry()
            self.app_settings["window_geometry"] = geometry
            self.app_settings["window_state"] = self.state()
        except Exception:
            pass
        try:
            os.makedirs(DATA_DIR, exist_ok=True)
            with open(SETTINGS_FILE, "w", encoding="utf-8") as f:
                json.dump(self.app_settings, f, indent=4)
        except Exception:
            pass

    def center_window(self, window, width, height):
        x = int((self.winfo_screenwidth() / 2) - (width / 2))
        y = int((self.winfo_screenheight() / 2) - (height / 2))
        window.geometry(f"{width}x{height}+{x}+{y}")

    def delete_current_server(self):
        if self.selected_server_index != -1:
            if self.proxy_process:
                self.stop_proxy()
            name_to_del = self.servers[self.selected_server_index]["name"]
            if self.app_settings.get("default_server") == name_to_del:
                self.app_settings["default_server"] = ""
                self.save_app_settings()
            del self.servers[self.selected_server_index]
            self.save_servers_to_file()
            self.selected_server_index = -1
            self.name_var.set(self.tr("server_not_selected"))
            self.host_var.set("—")
            self.ping_label.configure(text="")
            self.connect_btn.configure(state="disabled")
            self.ping_btn.configure(state="disabled")
            self.delete_btn.configure(state="disabled")
            self.default_btn.configure(state="disabled")
            self.update_server_list()

    def open_settings_dialog(self):
        dialog = ctk.CTkToplevel(self)
        dialog.attributes("-alpha", 0)
        dialog.title(self.tr("settings_title"))
        dialog.configure(fg_color=BG_COLOR)
        self.center_window(dialog, 500, 380)
        dialog.resizable(False, False)
        dialog.transient(self)

        container = ctk.CTkFrame(dialog, fg_color=CARD_COLOR, corner_radius=12, border_width=1, border_color=BORDER_COLOR)
        container.pack(expand=True, fill="both", padx=25, pady=25)

        switch = ctk.CTkSwitch(
            container, text=self.tr("autostart"), font=self.font_main, text_color=TEXT_MAIN,
            variable=self.autostart_var, command=self.toggle_autostart, switch_width=38, switch_height=20
        )
        switch.pack(pady=(25, 15), padx=25, anchor="w")

        lang_frame = ctk.CTkFrame(container, fg_color="transparent")
        lang_frame.pack(fill="x", padx=25, pady=(0, 20))
        ctk.CTkLabel(lang_frame, text=self.tr("language_label"), font=self.font_main, text_color=TEXT_MAIN).pack(side="left")
        self.lang_var = ctk.StringVar(value="Русский" if self.lang == "ru" else "English")
        lang_menu = ctk.CTkOptionMenu(
            lang_frame, variable=self.lang_var, values=["Русский", "English"], command=self.change_language,
            fg_color=SIDEBAR_COLOR, button_color=BORDER_COLOR, button_hover_color=ACTIVE_ITEM_COLOR
        )
        lang_menu.pack(side="right")

        dns_frame = ctk.CTkFrame(container, fg_color="transparent", border_width=1, border_color=BORDER_COLOR, corner_radius=8)
        dns_frame.pack(fill="x", padx=25, pady=(0, 20))

        ctk.CTkLabel(dns_frame, text=self.tr("dns_settings"), font=self.font_bold, text_color=TEXT_MAIN).pack(anchor="w", padx=15, pady=(10, 5))

        type_frame = ctk.CTkFrame(dns_frame, fg_color="transparent")
        type_frame.pack(fill="x", padx=15, pady=5)
        ctk.CTkLabel(type_frame, text=self.tr("dns_type"), font=self.font_main, text_color=TEXT_MUTED).pack(side="left")
        dns_type_var = ctk.StringVar(value=self.app_settings.get("dns_type", "system"))
        dns_type_menu = ctk.CTkOptionMenu(
            type_frame, variable=dns_type_var,
            values=[self.tr("dns_system"), self.tr("dns_doh"), self.tr("dns_dot")],
            fg_color=SIDEBAR_COLOR, button_color=BORDER_COLOR, button_hover_color=ACTIVE_ITEM_COLOR,
            width=150
        )
        dns_type_menu.pack(side="right")

        addr_frame = ctk.CTkFrame(dns_frame, fg_color="transparent")
        addr_frame.pack(fill="x", padx=15, pady=5)
        ctk.CTkLabel(addr_frame, text=self.tr("dns_server_address"), font=self.font_main, text_color=TEXT_MUTED).pack(side="left")
        dns_addr_var = ctk.StringVar(value=self.app_settings.get("dns_server", "https://1.1.1.1/dns-query"))
        dns_addr_entry = ctk.CTkEntry(addr_frame, textvariable=dns_addr_var, fg_color=SIDEBAR_COLOR, border_color=BORDER_COLOR)
        dns_addr_entry.pack(side="right", fill="x", expand=True, padx=(10, 0))

        dns_proxy_var = ctk.BooleanVar(value=self.app_settings.get("dns_through_proxy", True))
        dns_proxy_check = ctk.CTkCheckBox(
            dns_frame, text=self.tr("dns_through_proxy"), variable=dns_proxy_var,
            font=self.font_small, text_color=TEXT_MAIN
        )
        dns_proxy_check.pack(anchor="w", padx=15, pady=5)

        dns_test_btn = ctk.CTkButton(
            dns_frame, text=self.tr("dns_test"), font=self.font_small,
            fg_color="transparent", border_width=1, border_color=BORDER_COLOR,
            text_color=TEXT_MAIN, hover_color=SIDEBAR_COLOR, height=30
        )
        dns_test_btn.pack(anchor="w", padx=15, pady=(0, 10))
        dns_test_label = ctk.CTkLabel(dns_frame, text="", font=self.font_small, text_color=TEXT_MUTED)
        dns_test_label.pack(anchor="w", padx=15, pady=(0, 10))

        def update_dns_fields(*args):
            selected = dns_type_var.get()
            if selected == self.tr("dns_system"):
                dns_addr_entry.configure(state="disabled")
                dns_proxy_check.configure(state="disabled")
                dns_test_btn.configure(state="disabled")
            else:
                dns_addr_entry.configure(state="normal")
                dns_proxy_check.configure(state="normal")
                dns_test_btn.configure(state="normal")
        dns_type_var.trace_add("write", update_dns_fields)
        update_dns_fields()

        def test_dns():
            dns_type = dns_type_var.get()
            if dns_type == self.tr("dns_system"):
                dns_test_label.configure(text=self.tr("dns_test_success"), text_color=SUCCESS_COLOR)
                return
            server = dns_addr_var.get().strip()
            if not server:
                dns_test_label.configure(text=self.tr("dns_invalid_address"), text_color=DANGER_COLOR)
                return
            try:
                if dns_type == self.tr("dns_doh"):
                    import requests
                    response = requests.get(server, params={"name": "example.com", "type": "A"}, timeout=3)
                    if response.status_code == 200:
                        dns_test_label.configure(text=self.tr("dns_test_success"), text_color=SUCCESS_COLOR)
                    else:
                        dns_test_label.configure(text=self.tr("dns_test_fail"), text_color=DANGER_COLOR)
                elif dns_type == self.tr("dns_dot"):
                    import socket, ssl
                    context = ssl.create_default_context()
                    host = server.replace("tls://", "")
                    with socket.create_connection((host, 853), timeout=3) as sock:
                        with context.wrap_socket(sock, server_hostname=host) as ssock:
                            dns_test_label.configure(text=self.tr("dns_test_success"), text_color=SUCCESS_COLOR)
            except Exception:
                dns_test_label.configure(text=self.tr("dns_test_fail"), text_color=DANGER_COLOR)
        dns_test_btn.configure(command=test_dns)

        btn_save = ctk.CTkButton(
            container, text=self.tr("restart_app"), font=self.font_bold,
            fg_color=ACCENT_COLOR, hover_color=ACCENT_HOVER, height=38, corner_radius=8,
            command=lambda: self.save_dns_and_restart(dns_type_var, dns_addr_var, dns_proxy_var, dialog)
        )
        btn_save.pack(fill="x", padx=25, pady=(0, 15))

        logs_btn = ctk.CTkButton(
            container, text=self.tr("view_logs"), font=self.font_bold, fg_color=SIDEBAR_COLOR, hover_color=BORDER_COLOR,
            text_color=TEXT_MAIN, height=38, corner_radius=8, command=self.view_logs
        )
        logs_btn.pack(fill="x", padx=25, pady=(0, 25))

        dialog.after(150, lambda: [dialog.attributes("-alpha", 1), dialog.grab_set()])

    def save_dns_and_restart(self, dns_type_var, dns_addr_var, dns_proxy_var, dialog):
        selected_type = dns_type_var.get()
        if selected_type == self.tr("dns_system"):
            self.app_settings["dns_type"] = "system"
        elif selected_type == self.tr("dns_doh"):
            self.app_settings["dns_type"] = "doh"
        elif selected_type == self.tr("dns_dot"):
            self.app_settings["dns_type"] = "dot"
        self.app_settings["dns_server"] = dns_addr_var.get().strip()
        self.app_settings["dns_through_proxy"] = dns_proxy_var.get()
        self.save_app_settings()
        dialog.destroy()
        if self.proxy_process is not None:
            self.restart_proxy_if_needed()
        else:
            messagebox.showinfo(self.tr("settings_title"), self.tr("dns_apply_restart"))

    def view_logs(self):
        log_path = os.path.abspath(LOG_FILE)
        if os.path.exists(log_path):
            if os.name == 'nt': os.startfile(log_path)
            else:
                try: subprocess.call(['xdg-open', log_path])
                except Exception: pass
        else:
            messagebox.showinfo(self.tr("log_not_found"), self.tr("log_not_found"))

    def change_language(self, choice):
        self.app_settings["language"] = "ru" if choice == "Русский" else "en"
        self.save_app_settings()
        self.restart_app()

    def restart_app(self):
        self.cleanup_and_exit()
        python = sys.executable
        os.execl(python, python, *sys.argv)

    def open_import_dialog(self):
        dialog = ctk.CTkToplevel(self)
        dialog.attributes("-alpha", 0)
        dialog.title(self.tr("import_title"))
        dialog.configure(fg_color=BG_COLOR)
        self.center_window(dialog, 400, 220)
        dialog.resizable(False, False)
        dialog.transient(self)
        container = ctk.CTkFrame(dialog, fg_color=CARD_COLOR, corner_radius=12, border_width=1, border_color=BORDER_COLOR)
        container.pack(expand=True, fill="both", padx=20, pady=20)
        btn1 = ctk.CTkButton(
            container, text=self.tr("import_file"), font=self.font_bold, fg_color=SIDEBAR_COLOR, hover_color=BORDER_COLOR,
            text_color=TEXT_MAIN, height=42, corner_radius=8, command=lambda: [dialog.destroy(), self.import_sites_from_file()]
        )
        btn1.pack(pady=(25, 15), padx=25, fill="x")
        btn2 = ctk.CTkButton(
            container, text=self.tr("import_clipboard"), font=self.font_bold, fg_color=ACCENT_COLOR, hover_color=ACCENT_HOVER,
            height=42, corner_radius=8, command=lambda: [dialog.destroy(), self.add_from_clipboard()]
        )
        btn2.pack(padx=25, fill="x")
        dialog.after(150, lambda: [dialog.attributes("-alpha", 1), dialog.grab_set()])

    def get_app_path(self):
        if getattr(sys, 'frozen', False): return sys.executable
        return os.path.abspath(sys.argv[0])

    def check_autostart(self):
        key_path = r"Software\Microsoft\Windows\CurrentVersion\Run"
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_READ) as key:
                value, _ = winreg.QueryValueEx(key, "TaaClient")
                return value == self.get_app_path()
        except WindowsError: return False

    def toggle_autostart(self):
        key_path = r"Software\Microsoft\Windows\CurrentVersion\Run"
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_SET_VALUE) as key:
                if self.autostart_var.get():
                    winreg.SetValueEx(key, "TaaClient", 0, winreg.REG_SZ, self.get_app_path())
                else:
                    try: winreg.DeleteValue(key, "TaaClient")
                    except FileNotFoundError: pass
        except Exception: pass

    def import_sites_from_file(self):
        filepath = filedialog.askopenfilename(
            title=self.tr("select_file"), filetypes=[("Text Files", "*.txt"), ("All Files", "*.*")]
        )
        if filepath:
            try:
                with open(filepath, 'r', encoding='utf-8') as f: content = f.read()
                self.routing_textbox.configure(state="normal", fg_color=SIDEBAR_COLOR)
                self.routing_textbox.delete("1.0", "end")
                self.routing_textbox.insert("1.0", content)
                self.toggle_split_state()
                self.save_current_routes()
                self.restart_proxy_if_needed()
            except Exception: pass

    def check_ping_thread(self):
        if self.selected_server_index == -1: return
        self.ping_btn.configure(state="disabled")
        self.ping_label.configure(text=self.tr("ping_checking"), text_color=TEXT_MUTED)
        server = self.servers[self.selected_server_index]
        threading.Thread(target=self._perform_tcp_ping, args=(server['host'], server['port']), daemon=True).start()

    def _perform_tcp_ping(self, host, port, timeout=3):
        try:
            start_time = time.time()
            with socket.create_connection((host, int(port)), timeout=timeout):
                ms = round((time.time() - start_time) * 1000)
            self.after(0, lambda: self._update_ping_ui(ms))
        except: self.after(0, lambda: self._update_ping_ui(-1))

    def _update_ping_ui(self, ms):
        self.ping_btn.configure(state="normal")
        if ms == -1: self.ping_label.configure(text=self.tr("status_error"), text_color=DANGER_COLOR)
        else:
            color = SUCCESS_COLOR if ms < 150 else ("#F59E0B" if ms < 300 else DANGER_COLOR)
            self.ping_label.configure(text=f"{ms} ms", text_color=color)

    def update_host_display(self):
        if self.selected_server_index == -1: return
        server = self.servers[self.selected_server_index]
        port = server.get('port', '')
        if self.hide_host_var.get(): self.host_var.set(f"••••••••••••:{port}")
        else: self.host_var.set(f"{server['host']}:{port}")

    def toggle_split_state(self):
        if self.split_tunnel_var.get():
            self.routing_textbox.configure(state="normal", fg_color=SIDEBAR_COLOR)
            self.routes_combo.configure(state="readonly")
            self.new_routes_btn.configure(state="normal")
            self.delete_routes_btn.configure(state="normal")
            self.rename_routes_btn.configure(state="normal")
        else:
            self.routing_textbox.configure(state="disabled", fg_color=BG_COLOR)
            self.routes_combo.configure(state="disabled")
            self.new_routes_btn.configure(state="disabled")
            self.delete_routes_btn.configure(state="disabled")
            self.rename_routes_btn.configure(state="disabled")
        self.update_tray_menu()

    def on_split_toggle(self):
        self.save_current_routes()
        self.toggle_split_state()
        self.save_app_settings()
        self.restart_proxy_if_needed()

    def set_system_proxy(self, enable=True):
        path = r"Software\Microsoft\Windows\CurrentVersion\Internet Settings"
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, path, 0, winreg.KEY_WRITE) as key:
                if enable:
                    winreg.SetValueEx(key, "ProxyEnable", 0, winreg.REG_DWORD, 1)
                    winreg.SetValueEx(key, "ProxyServer", 0, winreg.REG_SZ, "127.0.0.1:1080")
                else: winreg.SetValueEx(key, "ProxyEnable", 0, winreg.REG_DWORD, 0)
            ctypes.windll.wininet.InternetSetOptionW(0, 37, 0, 0)
            ctypes.windll.wininet.InternetSetOptionW(0, 39, 0, 0)
        except: pass

    def load_servers_from_file(self):
        if os.path.exists(DB_FILE):
            try:
                with open(DB_FILE, "r", encoding="utf-8") as f: self.servers = json.load(f)
                self.update_server_list()
            except: pass

    def save_servers_to_file(self):
        os.makedirs(DATA_DIR, exist_ok=True)
        with open(DB_FILE, "w", encoding="utf-8") as f:
            json.dump(self.servers, f, indent=4, ensure_ascii=False)

    def load_routes(self):
        self.load_routes_from_file(self.current_routes_file)

    def save_routes(self):
        self.save_current_routes()

    def add_from_clipboard(self):
        try:
            url = self.clipboard_get().strip()
            if not (url.startswith("vless://") or url.startswith("hysteria2://")):
                return
            parsed = urllib.parse.urlparse(url)
            protocol = parsed.scheme
            params = dict(urllib.parse.parse_qsl(parsed.query))
            name = urllib.parse.unquote(parsed.fragment) if parsed.fragment else parsed.hostname
            server_data = {
                "type": protocol,
                "name": name,
                "host": parsed.hostname,
                "port": parsed.port,
                "params": params
            }
            if protocol == "vless":
                server_data["uuid"] = parsed.username
            elif protocol == "hysteria2":
                server_data["password"] = parsed.username
            self.servers.append(server_data)
            self.save_servers_to_file()
            self.update_server_list()
        except: pass

    def update_server_list(self):
        for w in self.server_list_frame.winfo_children(): w.destroy()
        self.server_buttons.clear()
        default_name = self.app_settings.get("default_server", "")
        for i, s in enumerate(self.servers):
            display_name = s["name"]
            if s["name"] == default_name:
                display_name += self.tr("default_marker")
            btn = ctk.CTkButton(
                self.server_list_frame, text=display_name, font=self.font_main, fg_color="transparent",
                text_color=TEXT_MAIN, hover_color=ACTIVE_ITEM_COLOR, anchor="w", height=38, corner_radius=8,
                command=lambda idx=i: self.select_server(idx)
            )
            btn.pack(pady=4, fill="x")
            self.server_buttons.append(btn)
        if self.selected_server_index != -1 and self.selected_server_index < len(self.server_buttons):
            self.server_buttons[self.selected_server_index].configure(fg_color=ACTIVE_ITEM_COLOR)
        self.update_tray_menu()

    def select_server(self, index):
        if self.selected_server_index != -1 and self.selected_server_index < len(self.server_buttons):
            self.server_buttons[self.selected_server_index].configure(fg_color="transparent")
        self.selected_server_index = index
        self.server_buttons[index].configure(fg_color=ACTIVE_ITEM_COLOR)
        s = self.servers[index]
        self.name_var.set(s["name"])
        self.ping_label.configure(text="")
        self.update_host_display()
        if self.app_settings.get("default_server") == s["name"]:
            self.default_btn.configure(text_color=SUCCESS_COLOR, state="normal")
        else:
            self.default_btn.configure(text_color=DANGER_COLOR, state="normal")
        self.connect_btn.configure(state="normal")
        self.ping_btn.configure(state="normal")
        self.delete_btn.configure(state="normal")
        self.update_tray_menu()

    def get_dns_config(self):
        dns_type = self.app_settings.get("dns_type", "system")
        if dns_type == "system":
            return {}

        server_address = self.app_settings.get("dns_server", "")
        if not server_address:
            return {}

        if dns_type == "doh":
            if not server_address.startswith("https://"):
                server_address = "https://" + server_address
            if not server_address.endswith("/dns-query"):
                if not server_address.endswith("/"):
                    server_address += "/"
                server_address += "dns-query"
        elif dns_type == "dot":
            if not server_address.startswith("tls://"):
                server_address = "tls://" + server_address

        dns_through_proxy = self.app_settings.get("dns_through_proxy", True)
        server_config = {
            "tag": "custom_dns",
            "address": server_address,
        }
        if dns_through_proxy:
            server_config["detour"] = "proxy"

        return {
            "servers": [server_config],
            "rules": [
                {
                    "outbound": "any",
                    "server": "custom_dns"
                }
            ]
        }

    def generate_singbox_config(self, server):
        p = server["params"]
        protocol_type = server.get("type", "vless")
        route_rules = []
        is_split = self.split_tunnel_var.get()
        final_outbound = "direct" if is_split else "proxy"
        if is_split:
            raw_routes = self.routing_textbox.get("1.0", "end-1c").strip()
            domains, ips = [], []
            items = [x.strip() for x in raw_routes.replace(',', '\n').split('\n') if x.strip()]
            for item in items:
                try:
                    if '/' in item:
                        ipaddress.ip_network(item, strict=False)
                        ips.append(item)
                    else:
                        ipaddress.ip_address(item)
                        ips.append(item + "/32")
                except: domains.append(item)
            if domains or ips:
                rule = {"outbound": "proxy"}
                if domains: rule["domain_suffix"] = domains
                if ips: rule["ip_cidr"] = ips
                route_rules.append(rule)
        if protocol_type == "hysteria2":
            main_outbound = {
                "type": "hysteria2",
                "tag": "proxy",
                "server": server["host"],
                "server_port": server["port"],
                "password": server.get("password", ""),
                "tls": {
                    "enabled": True,
                    "server_name": p.get("sni", ""),
                    "insecure": p.get("insecure", "0") == "1"
                }
            }
        else:
            main_outbound = {
                "type": "vless",
                "tag": "proxy",
                "server": server["host"],
                "server_port": server["port"],
                "uuid": server.get("uuid", ""),
                "packet_encoding": "xudp", "flow": p.get("flow", ""),
                "tls": {
                    "enabled": True, "server_name": p.get("sni", ""),
                    "utls": {"enabled": True, "fingerprint": p.get("fp", "chrome")},
                    "reality": {
                        "enabled": True, "public_key": p.get("pbk", ""), "short_id": p.get("sid", "")
                    } if p.get("security") == "reality" else None
                }
            }
        config = {
            "log": {"level": "info", "output": LOG_FILE},
            "inbounds": [{
                "type": "mixed", "listen": "127.0.0.1", "listen_port": 1080,
                "sniff": True, "sniff_override_destination": True
            }],
            "outbounds": [
                main_outbound,
                {"type": "direct", "tag": "direct"}
            ],
            "route": {"rules": route_rules, "final": final_outbound, "auto_detect_interface": True}
        }
        dns_config = self.get_dns_config()
        if dns_config:
            config["dns"] = dns_config

        os.makedirs(DATA_DIR, exist_ok=True)
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            json.dump(config, f, indent=2)

    def toggle_connection(self):
        if self.selected_server_index == -1 and self.proxy_process is None:
            return
        if self.proxy_process is None:
            if os.name == 'nt':
                try:
                    subprocess.run(
                        ["taskkill", "/f", "/im", "sing-box.exe"],
                        stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL,
                        creationflags=subprocess.CREATE_NO_WINDOW
                    )
                except Exception:
                    pass
            try:
                server = self.servers[self.selected_server_index]
                self.generate_singbox_config(server)
                time.sleep(0.3)
                si = subprocess.STARTUPINFO()
                si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
                si.wShowWindow = subprocess.SW_HIDE
                creation_flags = subprocess.CREATE_NO_WINDOW
                sb_path = resource_path("sing-box.exe")
                self.proxy_process = subprocess.Popen(
                    [sb_path, "run", "-c", CONFIG_FILE],
                    startupinfo=si,
                    creationflags=creation_flags,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    stdin=subprocess.DEVNULL,
                    close_fds=True
                )
                self.set_system_proxy(True)
                self.status_label.configure(text=self.tr("status_connected"), text_color=SUCCESS_COLOR)
                self.connect_btn.configure(text=self.tr("btn_disconnect"), fg_color=DANGER_COLOR, hover_color=DANGER_HOVER)
                if hasattr(self, 'tray_icon'):
                    self.tray_icon.icon = self.icon_on
                self.update_tray_menu()
            except Exception as e:
                self.status_label.configure(text=self.tr("status_error"), text_color=DANGER_COLOR)
                with open("error.log", "a") as f:
                    f.write(f"Ошибка запуска: {e}\n")
        else:
            self.stop_proxy()

    def stop_proxy(self):
        self.set_system_proxy(False)
        if self.proxy_process:
            self.proxy_process.terminate()
            self.proxy_process.wait()
            self.proxy_process = None
        self.status_label.configure(text=self.tr("status_disconnected"), text_color=TEXT_MUTED)
        self.connect_btn.configure(text=self.tr("btn_connect"), fg_color=ACCENT_COLOR, hover_color=ACCENT_HOVER)
        if hasattr(self, 'tray_icon'):
            self.tray_icon.icon = self.icon_off
        self.update_tray_menu()

if __name__ == "__main__":
    app = ProxyApp()
    app.mainloop()

Taa Client графический клиент для подключения к прокси-серверам типа VLESS и Hysteria2. Приложение запускает локальный прокси-сервер (через sing-box) и настраивает системный прокси Windows, позволяя маршрутизировать трафик через выбранный сервер.

1. Управление серверами
   - Добавление сервера из буфера обмена по ссылке вида `vless://...` или `hysteria2://...`.
   - Просмотр списка серверов на боковой панели.
   - Удаление сервера.
   - Выбор сервера по умолчанию (автоматическое подключение при запуске).
   - Тестирование задержки (TCP ping) до сервера.

2. Подключение/отключение
   - При нажатии «Подключиться» генерируется конфигурационный файл для sing-box с учётом выбранного типа прокси, настроек маршрутизации и DNS.
   - Запускается процесс sing-box, который слушает локальный порт 1080 (смешанный HTTP/SOCKS5).
   - Автоматически устанавливается системный прокси Windows на адрес 127.0.0.1:1080.
   - При отключении процесс завершается, системный прокси снимается.

3. Split Tunneling (сплит-туннелирование)
   - Позволяет направить через прокси только определённые сайты или IP-сети, а остальной трафик отправлять напрямую.
   - Можно управлять несколькими файлами списков (создавать, переименовывать, удалять).
   - Список поддерживает домены (например, `youtube.com`) и подсети CIDR (например, `192.168.0.0/24`).
   - При выключенном режиме весь трафик идёт через прокси.

4. Настройки DNS
   - Выбор типа DNS: системный (используется DNS Windows), DNS over HTTPS (DoH) или DNS over TLS (DoT).
   - Указание адреса DNS-сервера (например, `https://1.1.1.1/dns-query` или `tls://1.1.1.1`).
   - Опция «Направлять DNS через прокси» — если включена, DNS-запросы тоже будут проходить через прокси-соединение.
   - Проверка работоспособности выбранного DNS-сервера кнопкой «Проверить DNS».

5. Автозапуск
   - Приложение может автоматически запускаться вместе с Windows (добавление в реестр).

6. Интерфейс и локализация
   - Тёмная тема оформления.
   - Поддержка русского и английского языков, переключаемых в настройках.
   - Окно можно свернуть в системный трей, откуда доступны основные действия: подключиться/отключиться, выбрать сервер, включить split tunneling, развернуть окно или выйти.

7. Просмотр логов
   - Кнопка «Посмотреть логи» открывает файл `proxy.log` (в папке %LOCALAPPDATA%\TaaClient), куда sing-box пишет свою диагностику.

8. Импорт конфигураций
   - Импорт списка сайтов для split tunneling из текстового файла.

Как это работает 
- Все данные хранятся в `%LOCALAPPDATA%\TaaClient`:
  - `data/servers.json` — список серверов.
  - `data/settings.json` — настройки приложения.
  - `list/*.txt` — файлы списков split tunneling.
  - `proxy.log` — лог работы sing-box.
- При подключении:
  1. На основе выбранного сервера и текущих настроек (split tunneling, DNS) генерируется `data/config.json`.
  2. Запускается бинарник `sing-box.exe` (упакованный в ресурсы) с командой `run -c config.json`.
  3. Устанавливается системный прокси через реестр Windows и `InternetSetOption`.
- При отключении процесс завершается, прокси снимается.
- В трее иконка меняет цвет в зависимости от состояния (подключено/отключено).

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

Taa Client is a graphical client for connecting to VLESS and Hysteria2 proxy servers. It runs a local proxy server (via sing-box) and configures the Windows system proxy to route traffic through the selected server.


1. Server Management
   - Add a server from clipboard using a `vless://` or `hysteria2://` URL.
   - View the server list in the sidebar.
   - Delete a server.
   - Set a default server (auto‑selected on startup).
   - Test latency (TCP ping) to the server.

2. Connect / Disconnect
   - Clicking "Connect" generates a sing-box configuration file based on the selected server, routing settings, and DNS preferences.
   - The sing-box process starts, listening on local port 1080 (mixed HTTP/SOCKS5).
   - The Windows system proxy is automatically set to `127.0.0.1:1080`.
   - Disconnecting stops the process and disables the system proxy.

3. Split Tunneling
   - Route only specific websites or IP networks through the proxy; other traffic goes directly.
   - Multiple route lists can be managed (create, rename, delete).
   - The list supports domains (e.g., `youtube.com`) and CIDR subnets (e.g., `192.168.0.0/24`).
   - When split tunneling is disabled, all traffic goes through the proxy.

4. DNS Settings
   - Choose DNS type: system (Windows default), DNS over HTTPS (DoH), or DNS over TLS (DoT).
   - Specify the DNS server address (e.g., `https://1.1.1.1/dns-query` or `tls://1.1.1.1`).
   - Option "Route DNS through proxy" – if enabled, DNS queries also go through the proxy connection.
   - Test the selected DNS server with the "Test DNS" button.

5. Auto‑Start
   - The application can start automatically with Windows (adds an entry to the registry).

6. Interface and Localization
   - Dark theme.
   - Supports Russian and English languages, switchable in settings.
   - Minimizes to system tray; the tray menu provides quick actions: connect/disconnect, select server, toggle split tunneling, show window, or quit.

7. Log Viewer
   - The "View Logs" button opens the `proxy.log` file (in `%LOCALAPPDATA%\TaaClient`), which contains sing-box diagnostic output.

8. Import Configurations
   - Import a list of sites for split tunneling from a text file.

- All data is stored in `%LOCALAPPDATA%\TaaClient`:
  - `data/servers.json` – list of servers.
  - `data/settings.json` – application settings.
  - `list/*.txt` – split tunneling route files.
  - `proxy.log` – sing-box log.
- On connection:
  1. `data/config.json` is generated based on the selected server and current settings (split tunneling, DNS).
  2. The `sing-box.exe` binary (bundled in resources) is launched with `run -c config.json`.
  3. System proxy is enabled via Windows registry and `InternetSetOption`.
- On disconnection, the process is terminated and the proxy is disabled.
- The tray icon changes color based on connection state (connected/disconnected).

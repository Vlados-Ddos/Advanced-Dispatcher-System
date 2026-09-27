# Advanced Dispatcher System

### Description

Advanced Dispatcher System is a web dispatch console for Derail Valley. It reads the current railway state and presents topology, rolling stock, signals, turnouts, blocks, routes, orders and players on one map with an object inspector.

It is intended for route planning, conflict and reservation checks, traffic monitoring and supported infrastructure commands from a browser. It does not create a second railway topology or replace the game’s native signalling systems.

### Installation instructions

1. Install [**Unity Mod Manager**](https://www.nexusmods.com/site/mods/21).
2. Open the mod settings and click **Open dispatcher**.
3. The local owner signs in automatically. The default address is `http://127.0.0.1:7246/` unless the port or HTTPS setting was changed.

Close the game and dispatcher before replacing the mod. Never publish or share `Host/data`; it contains local accounts and server credentials.

### Connecting from another device or Multiplayer

#### Starting the dispatcher host

1. Start Derail Valley on the host computer.
2. Open UMM and enable **Advanced Dispatcher System**.
3. In the mod settings, choose the server port if needed. The default is `7246`.
4. Enable **Allow LAN access** when another computer or phone must connect.
5. Enable HTTPS for LAN or Internet access when possible, then apply the settings/restart the server.
6. Open the dispatcher on the host and create named `viewer` or `dispatcher` accounts in the web settings for remote users.

The local owner login is intended for the host computer and loopback access. Remote users should use a named web account. The game itself must be hosted normally in Derail Valley Multiplayer; Advanced Dispatcher does not create or replace the game session.

#### Connecting from another PC

1. Put the PC and the host on the same network, or configure TCP port forwarding to the host for an Internet connection.
2. Find the host’s IPv4 address with `ipconfig`.
3. Open a modern browser on the other PC and visit `http://HOST_IPV4:7246/`, or `https://HOST_IPV4:PORT/` when HTTPS is enabled.
4. Sign in with the web account created by the host.

Windows Firewall and router port forwarding are not configured automatically. HTTP does not encrypt credentials; use HTTPS for networks you do not fully trust.

#### Connecting from a phone or tablet

1. Connect the device to the same Wi-Fi network as the host.
2. Find the host’s local IPv4 address.
3. Open the device browser and visit `http://HOST_IPV4:7246/` with the configured port.
4. Sign in with a named web account.

#### Playing with a friend

The friend must be able to reach the host’s browser server. On a private LAN, use the host’s private IPv4 address. Across the Internet, configure router port forwarding and use the host’s public IPv4 address; the firewall must allow the selected TCP port. HTTPS is strongly recommended for Internet access.

The browser role controls dispatcher permissions. Multiplayer authority remains on the game host: a remote browser cannot become authority, bypass protected reservations or execute commands locally. If the compatible Multiplayer API is absent or unsupported, Multiplayer-specific controls are disabled.

### Main features

- Native railway topology and track geometry in game coordinates.
- Map layers for tracks, turnouts, main and shunting signals, blocks, reservations, routes, trains, players, stations, industries, passenger stops, turntables, speed information, signs and technical labels.
- Real signal aspects, lamps, direction, blinking state and shunting classification when DV Signals is available.
- Track Set To and Departure Allowed indicators from their real owner and state.
- Turnout branches and route highlights based on actual topology links.
- Track blocks with occupancy, reservation, direction, trains and linked signals when supplied by the game.
- Locomotives, wagons, coupled groups, tenders, traction sections and consist cargo.
- Orders with status, owner, cargo, cars, trains, destinations, tasks, payment, licences and history.
- Players and positions when supplied by the game or Multiplayer integration.
- Language-independent search, sorting, status filters, active-only filters and virtualized long lists.
- Route preview from a track or selected train to a destination track.
- Ordered intermediate route points selected on the map or in the track inspector.
- Route sequence, blocks, signals, turnouts, turntables, warnings and remaining distance.
- Exact-ID selection of the confirmed route after planning; the planning bar closes and Routes opens automatically.
- Advisory planning that can align turnouts while describing an occupied approach without claiming a reservation.
- Normal reservations using the game and DV Signals state.
- Protected reservations that lock conflicting route turnouts through native game and supported Multiplayer host protection.
- Safety checks for rolling stock occupying or approaching a turnout.
- Cleanup of partially prepared routes when a later command or reservation check fails.
- Supported remote turntable alignment; non-opposite exits require a separate train manoeuvre.
- Event log, route warning history, reconnect handling and server health information.
- Authenticated viewer, dispatcher and local host administrator roles.
- Optional DV Signals, Passenger Jobs, Double Track and Multiplayer adapters.

### Map and display settings

Use the wheel or `+`/`−` to zoom, drag to pan, `Home` to fit and `Escape` to deselect. Layers affect browser rendering only and never change game state.

Display settings include interface/text scale, labels, rolling-stock and signal sizes, track and occupancy widths, colours, theme, camera preferences and eleven independent LOD thresholds. LOD changes visual detail and hit testing only; it does not change routes, reservations or signal logic.

### Signals and semaphores

Signals, shunting signals, mechanical semaphores and additional railway signs are displayed from available game data and the optional DV Signals integration. Heads, sections, lamps, aspects, blinking, operation mode, shunting permission and reservation state are kept distinct.

Hiding a layer hides the map object only. Without compatible DV Signals assemblies, native signal blocks, reservations and aspect commands are unavailable while the base topology and infrastructure view remain usable.

### Turnouts, blocks and reservations

Routes use real directed track links and junction branches. Each required turnout is checked against its revision, protection locks and rolling-stock safety envelope before a native command is sent.

Advisory planning may describe an occupied approach and align its turnouts. Normal and protected reservations are stricter: a live foreign consist, unknown occupancy, overlapping route, protected signal reservation or conflicting protected branch rejects the reservation.

Normal reservations leave gameplay turnout controls available. A manual change releases the reservation and reports the mismatch. Protected reservations lock conflicting branches until release. Cancellation releases only reservations owned by that route.

### Locomotives, wagons and consists

Locomotives shows captured locomotive units and movement data. Wagons shows individual cars and coupled groups. Native classification keeps locomotives, tenders, traction sections and wagons distinct.

The consist inspector shows coupled members, length, speed, direction, current track and available cargo. Remote locomotive controls are restricted to the local host administrator and require the corresponding UMM and game capabilities.

### Orders

Orders provides active and historical views. Depending on game data, an order can show owner, lifecycle, cargo, assigned cars, train, origin, destination, tasks, payment, licences and event history. Passenger Jobs is optional; missing passenger integration does not disable the core dispatcher.

### Players and Multiplayer

Singleplayer uses the local host. With compatible Multiplayer, the host can expose connected players and supported replicated railway state to browser clients.

Browser roles and Multiplayer authority are separate. A browser client cannot become the game authority. Unsupported or incompatible APIs disable controls instead of executing them locally. A live Multiplayer session still requires manual acceptance.

### Search, sorting and filters

Search normalizes Russian and English text, case and spaces, and uses IDs, names and related display names where available. Lists provide name or recent sorting, status filters and active-only filters. Long lists use bounded virtual rendering.

### Localization

The interface follows the host game language and provides Russian and English strings. Dynamic names and railway states use centralized localization. Missing optional data is shown as unknown or unavailable.

### Compatibility

- **Double Track:** Fully compatible.
- **DV Signals:** Fully compatible.
- **Passenger Jobs:** Fully compatible.
- **Multiplayer:** Fully compatible.

---

# Advanced Dispatcher System - Русский

### Описание

Advanced Dispatcher System — web-диспетчерская для Derail Valley. Она получает текущее состояние железнодорожной сети и показывает топологию, подвижной состав, сигналы, стрелки, блоки, маршруты, заказы и игроков на общей карте и в инспекторе.

Мод предназначен для планирования маршрутов, проверки конфликтов и резерваций, наблюдения за движением и поддерживаемых команд инфраструктуре из браузера. Он не создаёт вторую топологию и не заменяет штатную сигнализацию игры.

### Установка

1. Установите [**Unity Mod Manager**](https://www.nexusmods.com/site/mods/21).
2. Откройте настройки мода и нажмите **Открыть диспетчерскую**.
3. Локальный владелец входит автоматически. Адрес по умолчанию — `http://127.0.0.1:7246/`, если порт или HTTPS не изменены.

Перед заменой мода закройте игру и диспетчерскую. Не публикуйте и не передавайте `Host/data`: там находятся локальные учётные записи и данные сервера.

### Подключение с другого устройства и Multiplayer

#### Запуск диспетчерской на хосте

1. Запустите Derail Valley на компьютере хоста.
2. Откройте UMM и включите **Advanced Dispatcher System**.
3. В настройках мода выберите порт сервера при необходимости. По умолчанию используется `7246`.
4. Включите **Разрешить доступ по LAN**, если подключаться будут другой компьютер или телефон.
5. Для LAN или Интернета по возможности включите HTTPS и примените настройки/перезапустите сервер.
6. Откройте диспетчерскую на хосте и создайте в web-настройках именные аккаунты `viewer` или `dispatcher` для удалённых пользователей.

Автоматический вход локального владельца предназначен для компьютера хоста и loopback-доступа. Удалённые пользователи должны использовать именной web-аккаунт. Саму игровую сессию нужно обычным образом создать в Derail Valley Multiplayer: Advanced Dispatcher не создаёт и не заменяет игровую сессию.

#### Подключение с другого компьютера

1. Подключите компьютер и хост к одной сети либо настройте перенаправление TCP-порта на хост для подключения через Интернет.
2. Узнайте IPv4-адрес хоста командой `ipconfig`.
3. Откройте современный браузер на другом компьютере и перейдите на `http://IPV4_ХОСТА:7246/` или на `https://IPV4_ХОСТА:ПОРТ/`, если включён HTTPS.
4. Войдите с помощью web-аккаунта, созданного владельцем хоста.

Windows Firewall и перенаправление порта на роутере автоматически не настраиваются. HTTP не шифрует учётные данные; для недоверенных сетей используйте HTTPS.

#### Подключение с телефона или планшета

1. Подключите устройство к той же Wi-Fi-сети, что и хост.
2. Узнайте локальный IPv4-адрес хоста.
3. Откройте браузер устройства и перейдите на `http://IPV4_ХОСТА:7246/` с настроенным портом.
4. Войдите с помощью именного web-аккаунта.

#### Игра с другом

Друг должен иметь сетевой доступ к web-серверу хоста. В локальной сети используйте частный IPv4-адрес хоста. Через Интернет настройте перенаправление TCP-порта и используйте публичный IPv4-адрес хоста; Firewall также должен разрешать выбранный порт. Для Интернета настоятельно рекомендуется HTTPS.

Роль web-аккаунта определяет права диспетчерской. Authority Multiplayer остаётся у игрового хоста: удалённый браузер не становится authority, не обходит защищённые резервации и не выполняет команды локально. При отсутствии или несовместимости Multiplayer API связанные controls отключаются.

### Основные возможности

- Реальная топология и геометрия путей в игровых координатах.
- Слои путей, стрелок, поездных и маневровых сигналов, блоков, резервов, маршрутов, поездов, игроков, станций, промышленных объектов, пассажирских остановок, кругов, скоростной информации, знаков и технических подписей.
- Реальные показания, лампы, направление, мигание и классификация маневровых сигналов при наличии DV Signals.
- Индикаторы Track Set To и Departure Allowed по их владельцу и состоянию.
- Ветки стрелок и подсветка маршрутов по реальным топологическим связям.
- Путевые блоки с занятостью, резервом, направлением, поездами и сигналами, если игра передаёт эти данные.
- Локомотивы, вагоны, сцепленные группы, тендеры, тяговые секции и груз состава.
- Заказы со статусом, владельцем, грузом, вагонами, поездами, назначениями, задачами, оплатой, лицензиями и историей.
- Игроки и их положение, если данные передаёт игра или Multiplayer.
- Поиск независимо от языка, сортировка, фильтры состояния, фильтр активных объектов и виртуализированные длинные списки.
- Предпросмотр маршрута от пути или выбранного поезда до пути назначения.
- Упорядоченные промежуточные точки с выбором на карте или в инспекторе пути.
- Последовательность маршрута, блоки, сигналы, стрелки, круги, предупреждения и оставшееся расстояние.
- После подтверждённого планирования нижняя панель закрывается, открываются «Маршруты» и выбирается точный маршрут.
- Advisory-планирование, которое может выровнять стрелки занятого подхода без создания резервации.
- Обычные резервации с использованием состояния игры и DV Signals.
- Защищённые резервации с блокировкой конфликтующих ветвей через игру и совместимый Multiplayer host.
- Проверка техники, стоящей на стрелке или приближающейся к ней.
- Очистка частично подготовленного маршрута при отказе следующей команды или проверки.
- Поддерживаемое удалённое выравнивание поворотных кругов; для непротивоположных выходов нужен отдельный манёвр.
- Журнал событий, история предупреждений маршрутов, переподключение и состояние сервера.
- Роли браузера viewer, dispatcher и локального администратора хоста.
- Необязательные адаптеры DV Signals, Passenger Jobs, Double Track и Multiplayer.

### Карта и настройки отображения

Масштаб меняется колёсиком или `+`/`−`, панорамирование — перетаскиванием, `Home` подгоняет сеть, `Escape` снимает выбор. Слои влияют только на отображение и не меняют состояние игры.

Настройки включают масштаб интерфейса и текста, подписи, размеры подвижного состава и сигналов, толщины путей и занятости, цвета, тему, камеру и одиннадцать независимых порогов LOD. LOD меняет только визуальные детали и hit-testing.

### Сигналы и семафоры

Сигналы, маневровые сигналы, механические семафоры и дополнительные знаки показываются по данным игры и необязательной интеграции DV Signals. Головки, секции, лампы, показания, мигание, режим, разрешение манёвров и резервация остаются отдельными состояниями.

Скрытие слоя скрывает объект только на карте. Без совместимого DV Signals недоступны нативные блоки, резервации и команды показаниям, но базовая топология и инфраструктура остаются доступны.

### Стрелки, блоки и резервации

Маршруты используют реальные направленные связи путей и ветви стрелок. Перед native-командой проверяются revision, блокировки и зона безопасности подвижного состава.

Advisory-план может описывать занятый подход и выровнять стрелки. Обычная и защищённая резервации строже: живой чужой состав, неизвестная занятость, пересечение маршрутов, защищённая резервация сигнала или конфликтующая защищённая ветвь отклоняют резервацию.

Обычная резервация оставляет ручное управление стрелками доступным. Ручное изменение снимает резерв и показывает несовпадение. Защищённая резервация блокирует конфликтующие ветви до снятия маршрута. Отмена освобождает только принадлежащие маршруту резервации.

### Локомотивы, вагоны и составы

«Локомотивы» показывает захваченные локомотивы и данные движения. «Вагоны» показывает отдельные машины и сцепленные группы. Нативная классификация различает локомотивы, тендеры, тяговые секции и вагоны.

Инспектор состава показывает сцепленные машины, длину, скорость, направление, текущий путь и доступный груз. Управление локомотивом доступно только локальному администратору хоста при наличии разрешений UMM и игры.

### Заказы

«Заказы» содержит активные и исторические записи. В зависимости от данных игры доступны владелец, состояние, груз, вагоны, поезд, начало, назначение, задачи, оплата, лицензии и история событий. Passenger Jobs необязателен.

### Игроки и Multiplayer

В одиночной игре используется локальный хост. При совместимом Multiplayer хост может передавать браузерным клиентам игроков и поддерживаемое реплицируемое состояние сети.

Роли браузера и authority Multiplayer разделены. Браузер не становится игровым хостом. Несовместимый API отключает controls вместо локального выполнения команд. Настоящую multiplayer-сессию нужно принять вручную в игре.

### Поиск, сортировка и фильтры

Поиск нормализует русский и английский текст, регистр и пробелы, использует ID, имена и связанные отображаемые имена. Списки поддерживают сортировку, фильтры состояния и активных объектов. Длинные списки используют ограниченный виртуальный рендеринг.

### Локализация

Интерфейс следует языку игры и содержит русские и английские строки. Динамические имена и состояния используют централизованную локализацию. Отсутствующие необязательные данные отображаются как неизвестные или недоступные.

### Совместимость

- **Double Track:** Полная совместимость.
- **DV Signals:** Полная совместимость.
- **Passenger Jobs:** Полная совместимость.
- **Multiplayer:** Полная совместимость.

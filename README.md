# Advanced Dispatcher System

### Description

Advanced Dispatcher System is a web dispatch console for Derail Valley. It reads the current railway state and presents topology, rolling stock, signals, turnouts, blocks, routes, orders and players on one map with an object inspector. It is intended for route planning, conflict and reservation checks, traffic monitoring and supported infrastructure commands from a browser.

### Installation instructions

1. Install [**Unity Mod Manager**](https://www.nexusmods.com/site/mods/21).
2. Open the mod settings and click **Open dispatcher**. This opens the browser and copies a one-use owner code.
3. On the first HTTPS visit, set up certificate trust as described below. Select **Owner access** on the sign-in page and paste the copied code within two minutes. If it has expired, click **Open dispatcher** again. An existing valid browser session opens directly.

The default address is `https://localhost:7246/`. With **Allow Remote LAN Access** disabled, use `http://localhost:7246/`; access is then restricted to the host computer. Use the configured port if you change it.

### Connecting from another device or Multiplayer

#### Starting the dispatcher host

1. Start Derail Valley on the host computer. For Multiplayer, start or join the game session through the Multiplayer mod as usual.
2. Enable **Advanced Dispatcher System** in UMM. For shared railway control, use the game host’s dispatcher.
3. Leave **Allow Remote LAN Access** enabled for connections from another computer or phone. It is enabled by default and controls both remote listening and HTTPS; there is no separate HTTPS switch.
4. If needed, change the port under **Port and additional address**. The default is `7246`. Apply connection changes with **Apply and restart**.
5. Open the dispatcher on the host. In **Settings → Web access → Access profiles**, create a named **Viewer** or **Dispatcher** profile for each remote user.
6. In the same settings group, expand **Web Host connection** and use the displayed LAN or Radmin VPN address. Connection information is kept here, rather than in a separate UMM address panel.

Owner access is available through localhost on the host computer. Remote users sign in with a named web profile. Web accounts are separate from in-game names, and Advanced Dispatcher does not create or replace the Multiplayer game session.

#### Connecting from another PC

1. Connect the client and host to the same LAN or Radmin VPN network.
2. Set up trust for the host’s public certificate on the client, as described below.
3. Open the address shown by the host, including `https://` and the port: for example, `https://192.168.1.10:7246/` on LAN or `https://26.12.34.56:7246/` through Radmin VPN.
4. Sign in with the profile name and password supplied by the host.

Use the host’s address, not the client’s own address or localhost. Windows Firewall must allow the selected TCP port. The mod does not change firewall rules or configure router port forwarding automatically.

#### Connecting from a phone or tablet

1. Connect the device to a network that can reach the host, normally the same Wi-Fi/LAN.
2. Set up certificate trust on the device.
3. Open the host’s LAN address, such as `https://192.168.1.10:7246/`.
4. Sign in with a named web profile.

A phone connected to Wi-Fi does not automatically have access to the PC’s Radmin VPN. Use the LAN address unless the device has a working route into that VPN.

#### Playing with a friend

On a private LAN, use the host’s LAN address. Through Radmin VPN, both computers must have access to the same VPN network; use the host’s Radmin address. For an Internet connection, configure TCP forwarding and firewall access, and enter the external DNS name or IP under **Port and additional address** before restarting Web Host.

The browser role controls dispatcher permissions. Multiplayer authority remains on the game host: a remote browser cannot become authority or bypass protected reservations. A dispatcher running on a game client can display available state, but shared railway commands require connection to the game host. Unsupported Multiplayer APIs disable the affected controls.

#### HTTPS and certificates

Remote access uses HTTPS and secure WebSocket connections automatically. Local and VPN IP addresses do not receive public certificate trust automatically: each installation creates and retains its own certificate authority and server certificate. The certificate covers localhost and the host addresses discovered at startup, including active LAN/Radmin IPv4 addresses. Restart Web Host after changing network interfaces or the additional address.

Before the first HTTPS login, use **Open public certificate** in UMM or obtain `Mods/AdvancedDispatcherSystem/Host/data/dispatcher-ca.cer` from the host. The owner can also download this public certificate and view its SHA-256 fingerprint in **Settings → Web access → Web Host connection** after signing in.

- **Windows:** import the public certificate into the current user’s **Trusted Root Certification Authorities** store. Firefox may use a separate certificate store.
- **iPhone/iPad:** install the certificate profile and enable trust for it in the device’s certificate trust settings.
- **Android:** use the device’s CA certificate installation settings; browser support and device policy determine whether user-installed certificates are accepted.

Only trust a host you trust, and verify the certificate fingerprint with its owner. Opening the certificate does not install trust automatically. Share only the public `.cer` file; never share `.pfx` files or the entire `Host/data` folder. Restarting the host preserves certificate identity, so normal restarts do not require setting up trust again. The mod cannot make a local certificate publicly trusted or remove a browser warning when the device does not trust it.

#### Web accounts and sessions

Access profiles have separate creation and editing modes. Select an existing profile to change its role or enable **Change password**. A new password must contain 10–128 characters; profile names are case-sensitive and allow 1–48 letters, digits, `_` or `-`, without spaces. Save changes explicitly. Deletion confirms the selected profile’s name. Editing or deleting a profile ends its active session; other profiles are retained. Profiles and passwords survive Web Host and game restarts.

- **Viewer:** view the railway, trains and orders; game commands are unavailable.
- **Dispatcher:** manage routes, orders and supported infrastructure commands. Profile administration and locomotive controls remain with the host owner.
- **Local owner:** administer profiles and use enabled locomotive controls, subject to game authority and the host’s settings.

One profile permits one active browser session. Tabs sharing the same browser cookie can share it; a second independent login receives an account-in-use message. Use separate profiles for different people. The idle timeout is **15 minutes**: user activity extends the session, while background health checks and incoming game updates do not. Reconnection is possible while the session remains valid; after expiry, sign in again. **Logout** releases the profile immediately. Web Host restart also requires signing in again.

The Web UI has bounded waits for API requests, WebSocket connection and initial state, with connection status and a retry option on failure. If the page itself cannot load because the address, port or HTTPS connection is unavailable, the browser reports that failure before the dispatcher can run.

### Main features

- Native railway topology and track geometry in game coordinates, including detected topology changes.
- Map layers for tracks, turnouts, main and shunting signals, blocks, reservations, routes, trains, players, stations, industries, passenger stops, turntables, speed information, signs and technical labels.
- Real signal aspects, lamps, direction, blinking state and shunting classification when DV Signals is available.
- Track Set To and Departure Allowed indicators where provided by the signal pack.
- Turnout branches and route highlights based on actual topology links.
- Track blocks with occupancy, reservation, direction, trains and linked signals when supplied by the integration.
- Locomotives, wagons, coupled groups, tenders, traction sections, consist length, mass and cargo.
- Current orders with status, owner or assigned player, cargo, cars, destinations, task progress, payment and licences where available.
- Players and reported facing directions, including their position while aboard a locomotive or wagon.
- Language-independent search, sorting, status filters, active-only filters and virtualized long lists.
- Route preview from a track or selected consist, with ordered intermediate points and job-related required stops.
- Route sequence, blocks, signals, turnouts, turntables, warnings and remaining distance.
- Route assembly and turnout alignment without automatically claiming a reservation.
- Normal and protected reservations, with optional native DV Signals block protection.
- Automatic release of passed route sections based on the complete consist, including its last car.
- Proposed route changes after relevant network or occupancy changes, with dispatcher confirmation.
- Safety checks for rolling stock occupying or approaching a turnout, and cleanup after a failed route preparation.
- Supported remote turntable alignment; non-opposite exits require a separate train manoeuvre.
- Current weather, rail wetness, game time and the forecast supplied by the game.
- Event log with export, route warning history, reconnect handling and server health information.
- Russian and English UI, desktop panels and a mobile layout with touch map controls.
- Authenticated access profiles, host-controlled settings and restore-defaults actions.

### Map and display settings

Use the wheel or `+`/`−` to zoom, drag to pan, `Home` to fit and `Escape` to deselect while the map has focus. Select an object to open its details, follow moving objects or centre the camera on them. Related-object links and back navigation connect the inspectors. Route focus provides a clearer view of the selected path.

The interface includes Locations, Locomotives, Cars, Signals, Switches, Turntables, Track Blocks, Routes, Orders, Players, Tracks, Signs, Events, Settings and Weather. Desktop panel widths can be adjusted. Display settings include interface/text scale, labels, rolling-stock and signal sizes, track and occupancy widths, colours, theme, camera preferences and eleven independent LOD thresholds.

Layers affect browser rendering only and never change game state. Signs, speed limits and speed sections can be toggled independently. LOD changes visual detail and hit testing only; it does not change routes, reservations or signal logic.

#### Mobile interface

The mobile layout uses bottom navigation for Map, Routes, Consists and Orders, with the other sections under **More sections**. Drag the map to pan and pinch to zoom. Drag the details-panel handle up or down, or tap it, to expand or collapse the panel; its contents scroll separately. Action notifications appear near the bottom centre above navigation. The layout adapts to portrait and landscape views.

### Signals and semaphores

Signals, shunting signals, mechanical semaphores and additional railway signs are displayed from available game data and the optional DV Signals integration. Heads, sections, lamps, aspects, blinking, operation mode, shunting permission and reservation state are kept distinct. Supported commands are offered in the inspector according to the signal type, integration state and user permissions.

Signal-state collection includes handling for distant signals and Multiplayer state; it does not rely solely on a nearby visible lamp. Track Set To and Departure Allowed reflect their native owners and state; they are not independent manual controls. Shunting heads remain visible and can be inspected separately.

Hiding a layer hides the map object only. Without compatible DV Signals, native signal blocks, signal reservations and aspect commands are unavailable. Base tracks, turnouts and AD’s own route reservations remain usable where the game capabilities permit them.

### Turnouts, blocks and reservations

Routes use real directed track links and junction branches. Each required turnout is checked against its current state, protection locks and rolling-stock safety envelope before a native command is sent. With click-to-switch enabled, clicking a turnout on the map requests a switch change; **Alt+click** opens its details. The setting can be disabled, and the inspector also offers supported controls.

#### Planning and assembling a route

1. Select a starting track and use **Route from here**, or begin from a consist or an eligible order task.
2. Select tracks and add intermediate points in the required order; set the destination with **Set as destination**. Intermediate points can be reordered or removed.
3. Review the preview and warnings. **Avoid other route reservations** influences route search; turning it off does not remove reservation protections or occupancy checks.
4. Click **Build route**. The host rechecks current game state and aligns supported infrastructure. On confirmation, the route appears in Routes and its details open.
5. Choose **Reserve normally** or **Protected reservation** when a reservation is needed.

When the planner returns a staged route, its details show the current and next legs. After completing a leg, build the next one from the route details using the current train position and network state. This supports paths that cannot hold all required infrastructure settings as one continuous reservation; it is not an automatic yard-manoeuvre planner.

Building a route does not drive the train. An unreserved plan may describe an occupied approach, while turnout movement still follows safety checks. Reservations additionally check current occupancy, conflicting routes and native signal protection. Some ordinary route overlaps offer an explicit confirmation; this cannot bypass protected reservations or unsafe occupied infrastructure. Compatible routes for the same owner and unchanged consist can share protection where their direction and infrastructure settings agree.

Normal reservations leave gameplay turnout controls available. A conflicting manual change invalidates the reservation and reports the mismatch. Protected reservations lock conflicting branches while protection is held. Cancellation releases only protection owned by that route. Turntable commands check the bridge and clearance area; a connection requiring a change of direction or a non-opposite exit needs a separate manoeuvre.

#### Automatic route release and route changes

For routes linked to a consist, passed sections are released using the positions and physical footprint of all its cars, not only the leading locomotive. Native signal-block protection can remain until the whole block is clear, even when an individual passed track is no longer shown as reserved. A train standing on the route’s final section may therefore still hold protection. Routes without an associated consist need manual release or completion.

Coupling additional vehicles updates the tracked membership when the existing route cars remain together. Splitting the consist, removing a required car or derailing it can interrupt the route. Temporarily suspended cars are treated separately from confirmed removal.

Relevant changes can generate a proposed replacement path for the same route. Inspect and confirm or discard it in the route details; confirmation rechecks the live network and reservation. An intact existing reservation is retained until replacement succeeds. If no alternative is found, the reason is shown instead of silently creating a different route. Completed and cancelled routes remain available in route history.

### Locomotives, wagons and consists

Locomotives shows individual locomotive units and movement data. Cars shows individual wagons and coupled groups. Native classification keeps locomotives, tenders, traction sections and wagons distinct; coupling and uncoupling update their membership. Locomotive presentation uses available model names and catalogue colours.

The inspectors show individual and total consist mass, length, speed, direction, current track, coupled members, cargo and available traction data. Multiple locomotives and mixed consists are represented. Unknown mass or traction remains marked unavailable rather than being treated as zero.

The load-rating assessment uses available catalogue data and known dry/wet rail conditions. It is a reference at the displayed gradient, not a full simulation of gradients and traction over the planned route. Weather and load data must be available for an assessment.

Remote locomotive and coupling controls are restricted to the local host administrator and require the corresponding UMM and game capabilities. They do not provide an automatic train-driving or yard-shunting system.

### Orders

Orders shows the current available and in-progress jobs reported by the game. New jobs appear, changed jobs update, and completed, abandoned, expired or removed jobs leave the current list. Depending on game data, an order shows its owner or assigned player, cargo, cars, consist, origin, destination, tasks, payment, bonus timing and licences. The event log records supported job events; Orders is not a permanent archive of removed jobs.

Where permissions and game support allow it, the inspector offers job acceptance, cancellation, player assignment and routing for the current task. Passenger routes use required stops from Passenger Jobs, and dispatcher waypoints can be added while preserving those stops. Base-game locations remain available when Passenger Jobs is absent, without a warning about the optional mod.

Prepared single transport legs of shunting jobs can be routed when the required cars and locomotive form a confirmed eligible consist. Automatic extraction of trapped wagons, running around a train and multi-move yard rearrangement are not implemented.

Persistent Jobs suspension is distinguished from actual deletion: temporarily unavailable cars or jobs are marked accordingly, and actions needing live data remain unavailable until that data returns.

### Weather and forecast

Weather shows current conditions, available rain/cloud/fog/thunder measurements, rail wetness and the game’s time. The forecast displays the current and upcoming periods supplied by the game, with their times and duration. Conditions and icons follow native weather data; this is not an external weather service.

When a game API does not provide current conditions or a forecast, the missing data is shown as unavailable. Weather is used for the dry/wet rail load-rating reference; the dispatcher does not change the game’s weather.

### Players and Multiplayer

Singleplayer uses the local host. With compatible Multiplayer, the host can expose connected players and supported replicated railway state to browser clients. Player markers use the reported position and facing direction, with vehicle-relative positioning while aboard a car. Assigning a player to an order or route records dispatch responsibility; it does not drive that player’s train.

Browser roles and Multiplayer authority are separate. A browser client cannot become the game authority. Unsupported or incompatible APIs disable affected controls instead of executing them locally. All web users connected to the same Web Host receive that host’s state.

### Mod settings and defaults

UMM groups the settings into Web Dispatcher, Connection, Dispatcher permissions and Performance. The host controls remote access, port, additional address, read-only operation, owner locomotive controls and whether undiscovered locomotives are shown. Multiplayer clients see received host values as read-only. **Data collection per frame** is local to each computer; higher values collect data faster but use more game-frame time.

Connection edits remain a draft until **Apply and restart**. The same button offers **Restart Web Host** when nothing has changed. Other operational settings save when changed. **Restore defaults** resets mod settings after confirmation; on a Multiplayer client it resets only local performance settings.

Web display preferences are local to the browser. **Reset browser settings** restores appearance, layers, LOD, colours, panels, camera and navigation, then reloads the page. It does not delete web profiles, passwords or the existing session.

### Search, sorting and filters

Search normalizes Russian and English text, case and spaces, and uses IDs, names and related display names where available. Lists provide sorting, status filters and active-only filters appropriate to the selected section. Long lists use bounded virtual rendering.

### Compatibility

- **Double Track:** Fully compatible.
- **DV Signals:** Fully compatible.
- **Passenger Jobs:** Fully compatible.
- **Persistent Jobs:** Fully compatible.
- **Multiplayer:** Fully compatible.

---

# Advanced Dispatcher System - Русский

### Описание

Advanced Dispatcher System — web-диспетчерская для Derail Valley. Она получает текущее состояние железнодорожной сети и показывает топологию, подвижной состав, сигналы, стрелки, блоки, маршруты, заказы и игроков на общей карте и в инспекторе. Мод предназначен для планирования маршрутов, проверки конфликтов и резерваций, наблюдения за движением и поддерживаемых команд инфраструктуре из браузера.

### Установка

1. Установите [**Unity Mod Manager**](https://www.nexusmods.com/site/mods/21).
2. Откройте настройки мода и нажмите **Открыть диспетчерскую**. Кнопка открывает браузер и копирует одноразовый код владельца.
3. Перед первым входом по HTTPS настройте доверие к сертификату по инструкции ниже. На странице входа выберите **Вход владельца** и вставьте скопированный код в течение двух минут. Если код истёк, снова нажмите **Открыть диспетчерскую**. При действующем сеансе браузер открывает диспетчерскую сразу.

Адрес по умолчанию — `https://localhost:7246/`. Если **Разрешить удалённый доступ по LAN** выключено, используется `http://localhost:7246/`, а доступ возможен только с компьютера хоста. При изменении порта используйте новое значение.

### Подключение с другого устройства и Multiplayer

#### Запуск диспетчерской на хосте

1. Запустите Derail Valley на компьютере хоста. Для Multiplayer создайте игровую сессию или подключитесь к ней обычным способом через Multiplayer-мод.
2. Включите **Advanced Dispatcher System** в UMM. Для совместного управления сетью используйте диспетчерскую игрового хоста.
3. Для подключения другого компьютера или телефона оставьте **Разрешить удалённый доступ по LAN** включённым. Настройка включена по умолчанию и одновременно управляет удалённым доступом и HTTPS; отдельного переключателя HTTPS нет.
4. При необходимости измените порт в разделе **Порт и дополнительный адрес**. По умолчанию используется `7246`. Примените сетевые изменения кнопкой **Применить и перезапустить**.
5. Откройте диспетчерскую на хосте. В **Настройках → Доступ к диспетчерской → Профили доступа** создайте для каждого удалённого пользователя профиль **Наблюдатель** или **Диспетчер**.
6. В той же группе настроек раскройте **Подключение к Web Host** и используйте показанный LAN- или Radmin VPN-адрес. Информация о подключении находится здесь, а не в отдельной панели адресов UMM.

Вход владельца доступен через localhost на компьютере хоста. Удалённые пользователи входят через именные web-профили. Web-аккаунты не связаны с игровыми никами, а Advanced Dispatcher не создаёт и не заменяет игровую сессию Multiplayer.

#### Подключение с другого компьютера

1. Подключите клиент и хост к одной LAN-сети или сети Radmin VPN.
2. Настройте доверие к публичному сертификату хоста на клиенте по инструкции ниже.
3. Откройте показанный хостом адрес вместе с `https://` и портом: например, `https://192.168.1.10:7246/` для LAN или `https://26.12.34.56:7246/` для Radmin VPN.
4. Войдите с именем профиля и паролем, выданными владельцем хоста.

Используйте адрес хоста, а не собственный адрес клиента или localhost. Windows Firewall должен разрешать выбранный TCP-порт. Мод не меняет правила Firewall и не настраивает перенаправление портов на роутере автоматически.

#### Подключение с телефона или планшета

1. Подключите устройство к сети, из которой доступен хост, обычно к той же Wi-Fi/LAN-сети.
2. Настройте доверие к сертификату на устройстве.
3. Откройте LAN-адрес хоста, например `https://192.168.1.10:7246/`.
4. Войдите с помощью именного web-профиля.

Телефон в Wi-Fi не получает доступ к Radmin VPN компьютера автоматически. Используйте LAN-адрес, если у устройства нет работающего маршрута в эту VPN.

#### Игра с другом

В локальной сети используйте LAN-адрес хоста. Для Radmin VPN оба компьютера должны иметь доступ к одной VPN-сети; подключайтесь по Radmin-адресу хоста. Для Интернета настройте перенаправление TCP-порта и доступ в Firewall, а внешнее DNS-имя или IP укажите в разделе **Порт и дополнительный адрес** перед перезапуском Web Host.

Роль web-профиля определяет права диспетчерской. Управление Multiplayer остаётся у игрового хоста: удалённый браузер не становится игровым хостом и не обходит защищённые резервации. Диспетчерская игрового клиента может показывать доступные данные, но для управления общей сетью нужно подключиться к диспетчерской хоста. При несовместимом Multiplayer API связанные команды недоступны.

#### HTTPS и сертификаты

Удалённый доступ автоматически использует HTTPS и защищённое WebSocket-соединение. Локальные и VPN-адреса не получают публичное доверие к сертификату автоматически: каждая установка создаёт и сохраняет собственный удостоверяющий и серверный сертификаты. Сертификат включает localhost и адреса хоста, обнаруженные при запуске, в том числе активные IPv4 LAN/Radmin. После смены сетевых интерфейсов или дополнительного адреса перезапустите Web Host.

Перед первым HTTPS-входом нажмите **Открыть публичный сертификат** в UMM либо получите у владельца файл `Mods/AdvancedDispatcherSystem/Host/data/dispatcher-ca.cer`. После входа владелец также может скачать публичный сертификат и посмотреть его SHA-256 в **Настройках → Доступ к диспетчерской → Подключение к Web Host**.

- **Windows:** импортируйте публичный сертификат в **Доверенные корневые центры сертификации** текущего пользователя. Firefox может использовать отдельное хранилище сертификатов.
- **iPhone/iPad:** установите профиль сертификата и включите доверие к нему в настройках доверия сертификатам устройства.
- **Android:** используйте настройки установки сертификатов CA; принятие пользовательских сертификатов зависит от браузера и политики устройства.

Доверяйте только знакомому хосту и сверяйте отпечаток сертификата с его владельцем. Открытие сертификата не устанавливает доверие автоматически. Передавайте только публичный `.cer`, никогда не передавайте `.pfx` или всю папку `Host/data`. Обычный перезапуск сохраняет сертификаты и не требует заново настраивать доверие. Мод не может сделать локальный сертификат публично доверенным или убрать предупреждение браузера, если устройство ему не доверяет.

#### Web-профили и сеансы

У профилей доступа отдельные режимы создания и редактирования. Выберите существующий профиль для изменения роли или включите **Изменить пароль**. Новый пароль должен содержать 10–128 символов; имя профиля — 1–48 букв, цифр, `_` или `-`, без пробелов, с учётом регистра. Изменения нужно сохранить. Перед удалением подтверждается имя выбранного профиля. Изменение или удаление завершает его активный сеанс; остальные профили сохраняются. Профили и пароли сохраняются после перезапуска Web Host и игры.

- **Наблюдатель:** просмотр сети, поездов и заказов; игровые команды недоступны.
- **Диспетчер:** управление маршрутами, заказами и поддерживаемой инфраструктурой. Профили и управление локомотивами остаются у владельца хоста.
- **Локальный владелец:** управление профилями и разрешёнными органами локомотива с учётом полномочий в игре и настроек хоста.

Один профиль допускает один активный сеанс браузера. Вкладки с общей cookie браузера могут использовать этот сеанс; второй независимый вход получает сообщение, что учётная запись уже используется. Для разных людей создавайте отдельные профили. Таймаут бездействия — **15 минут**: действия пользователя продлевают сеанс, фоновые проверки связи и входящие обновления игры — нет. Переподключение возможно до истечения сеанса; после этого нужен повторный вход. **Выйти** освобождает профиль сразу. После перезапуска Web Host также нужно войти заново.

Ожидание API, WebSocket и начальных данных ограничено по времени; при сбое Web UI показывает состояние соединения и возможность повторить подключение. Если из-за адреса, порта или HTTPS не открывается сама страница, ошибку показывает браузер ещё до запуска диспетчерской.

### Основные возможности

- Реальная топология и геометрия путей в игровых координатах с обнаружением изменений сети.
- Слои путей, стрелок, поездных и маневровых сигналов, блоков, резервов, маршрутов, поездов, игроков, станций, промышленных объектов, пассажирских остановок, кругов, скоростной информации, знаков и технических подписей.
- Реальные показания, лампы, направление, мигание и классификация маневровых сигналов при наличии DV Signals.
- Индикаторы Track Set To и Departure Allowed, если они предусмотрены набором сигналов.
- Ветки стрелок и подсветка маршрутов по реальным топологическим связям.
- Путевые блоки с занятостью, резервом, направлением, поездами и сигналами, если их передаёт интеграция.
- Локомотивы, вагоны, сцепленные группы, тендеры, тяговые секции, длина, масса и груз состава.
- Текущие заказы со статусом, владельцем или назначенным игроком, грузом, вагонами, назначениями, задачами, оплатой и лицензиями при наличии данных.
- Игроки и переданное игрой направление взгляда, в том числе положение внутри локомотива или вагона.
- Поиск независимо от языка, сортировка, фильтры состояния, фильтр активных объектов и виртуализированные длинные списки.
- Предпросмотр маршрута от пути или состава с упорядоченными промежуточными точками и обязательными остановками задания.
- Последовательность маршрута, блоки, сигналы, стрелки, круги, предупреждения и оставшееся расстояние.
- Сборка маршрута и выставление стрелок без автоматического создания резервации.
- Обычные и защищённые резервации с дополнительной защитой нативных блоков при наличии DV Signals.
- Автоматический разбор пройденных участков с учётом всего состава, включая последний вагон.
- Предложения изменения маршрута при значимых изменениях сети или занятости, с подтверждением диспетчера.
- Проверка техники, стоящей на стрелке или приближающейся к ней, и очистка при неудачной подготовке маршрута.
- Поддерживаемое удалённое выравнивание поворотных кругов; для непротивоположных выходов нужен отдельный манёвр.
- Текущая погода, влажность рельсов, игровое время и прогноз, предоставленный игрой.
- Журнал событий с экспортом, история предупреждений маршрутов, переподключение и состояние сервера.
- Русский и английский интерфейс, панели для компьютера и мобильная компоновка с touch-управлением картой.
- Профили доступа, настройки под управлением хоста и возврат к значениям по умолчанию.

### Карта и настройки отображения

Масштаб меняется колёсиком или `+`/`−`, панорамирование — перетаскиванием, `Home` подгоняет сеть, `Escape` снимает выбор, когда карта в фокусе. Выберите объект для открытия сведений, центрирования камеры или слежения за движущимся объектом. Ссылки на связанные объекты и возврат назад связывают инспекторы. Режим просмотра отдельного маршрута позволяет лучше рассмотреть его путь.

В интерфейсе доступны Локации, Локомотивы, Вагоны, Сигналы, Стрелки, Поворотные круги, Путевые блоки, Маршруты, Заказы, Игроки, Пути, Знаки, События, Настройки и Погода. На компьютере можно менять ширину панелей. Настройки отображения включают масштаб интерфейса и текста, подписи, размеры подвижного состава и сигналов, толщины путей и занятости, цвета, тему, камеру и одиннадцать независимых порогов LOD.

Слои влияют только на отображение и не меняют состояние игры. Знаки, ограничения скорости и скоростные участки включаются отдельно. LOD меняет визуальную детализацию и выбор объектов мышью, но не логику маршрутов, резерваций и сигналов.

#### Мобильный интерфейс

На мобильном экране снизу расположены Карта, Маршруты, Составы и Заказы; остальные вкладки открываются через **Другие разделы**. Перетаскивание перемещает карту, жест двумя пальцами меняет масштаб. Потяните ручку панели сведений вверх или вниз либо нажмите на неё, чтобы раскрыть или свернуть панель; содержимое прокручивается отдельно. Уведомления о действиях появляются снизу по центру над навигацией. Компоновка адаптируется к вертикальной и горизонтальной ориентации.

### Сигналы и семафоры

Сигналы, маневровые сигналы, механические семафоры и дополнительные знаки показываются по данным игры и необязательной интеграции DV Signals. Головки, секции, лампы, показания, мигание, режим, разрешение манёвров и резервация остаются отдельными состояниями. Доступные команды в инспекторе зависят от типа сигнала, состояния интеграции и прав пользователя.

Сбор состояния учитывает удалённые сигналы и Multiplayer и не опирается только на видимую рядом лампу. Track Set To и Departure Allowed отображают нативные данные своих владельцев; это не отдельные ручные переключатели. Маневровые головы остаются видимыми и доступны для отдельного просмотра.

Скрытие слоя скрывает объект только на карте. Без совместимого DV Signals недоступны его нативные сигнальные блоки, резервации сигналов и команды показаниям. Базовые пути, стрелки и собственные резервации маршрутов AD остаются доступны в пределах возможностей игры.

### Стрелки, блоки и резервации

Маршруты используют реальные направленные связи путей и ветви стрелок. Перед командой проверяются текущее состояние стрелки, блокировки и зона безопасности подвижного состава. При включённом переводе кликом нажатие на стрелку на карте запрашивает её перевод; **Alt+click** открывает сведения. Настройку можно отключить; поддерживаемые команды также есть в инспекторе.

#### Планирование и сборка маршрута

1. Выберите начальный путь и нажмите **Начало маршрута** либо начните маршрут от состава или доступной задачи заказа.
2. Выбирайте пути и добавляйте промежуточные точки в нужном порядке; назначение задаётся кнопкой **Установить конечной точкой**. Промежуточные точки можно переставлять и удалять.
3. Проверьте предпросмотр и предупреждения. **Избегать чужих резерваций** влияет на поиск пути; отключение опции не снимает защиту резерваций и проверки занятости.
4. Нажмите **Собрать маршрут**. Хост повторно проверит состояние игры и выставит поддерживаемую инфраструктуру. После подтверждения маршрут появится в списке «Маршруты» и откроются его сведения.
5. При необходимости выберите **Обычная резервация** или **Защищённая резервация**.

Если планировщик возвращает поэтапный маршрут, в его сведениях показаны текущий и следующий этапы. После завершения этапа соберите следующий из сведений о маршруте с учётом текущего положения поезда и состояния сети. Так обрабатываются пути, для которых нельзя удерживать все необходимые настройки инфраструктуры одной непрерывной резервацией; это не автоматический планировщик маневровой работы.

Сборка маршрута не управляет движением поезда. План без резервации может описывать занятый подход, но перевод стрелок всё равно проходит проверки безопасности. Резервации дополнительно проверяют текущую занятость, конфликтующие маршруты и нативную защиту сигналов. Для некоторых пересечений обычных маршрутов доступно явное подтверждение; оно не обходит защищённые резервации и опасную занятость инфраструктуры. Совместимые маршруты одного владельца для неизменившегося состава могут совместно использовать защиту при совпадении направления и настроек инфраструктуры.

Обычная резервация оставляет ручное управление стрелками в игре доступным. Конфликтующее ручное изменение нарушает резервацию и показывает причину. Защищённая резервация блокирует конфликтующие ветви, пока действует защита. Отмена освобождает только принадлежащую маршруту защиту. Команды поворотным кругам проверяют мост и зону габарита; соединение со сменой направления или непротивоположным выходом требует отдельного манёвра.

#### Автоматический разбор и изменение маршрута

Для маршрута, связанного с составом, пройденные участки освобождаются по положению и габариту всех вагонов, а не только головного локомотива. Нативная защита сигнального блока может оставаться до освобождения всего блока, даже если отдельный пройденный путь уже не отмечен как зарезервированный. Поэтому состав, стоящий на конечном участке, ещё может удерживать защиту. Маршруты без привязанного состава освобождаются или завершаются вручную.

При прицепке дополнительных машин отслеживаемое членство обновляется, если исходные вагоны маршрута остаются вместе. Разделение состава, удаление нужного вагона или сход с рельсов могут прервать маршрут. Временная приостановка вагонов обрабатывается отдельно от подтверждённого удаления.

Значимые изменения могут вызвать предложение нового пути для того же маршрута. Рассмотрите его и подтвердите либо отклоните в сведениях о маршруте; подтверждение повторно проверяет сеть и резервацию. Действующий прежний резерв сохраняется до успешной замены. Если обхода нет, показывается причина, а другой маршрут не создаётся незаметно. Завершённые и отменённые маршруты доступны в истории маршрутов.

### Локомотивы, вагоны и составы

«Локомотивы» показывает отдельные локомотивы и данные движения. «Вагоны» показывает отдельные машины и сцепленные группы. Нативная классификация различает локомотивы, тендеры, тяговые секции и вагоны; сцепка и расцепка обновляют состав групп. Для локомотивов используются доступные названия моделей и каталоговые цвета.

Инспекторы показывают собственную и общую массу состава, длину, скорость, направление, текущий путь, сцепленные машины, груз и доступные данные о тяге. Учитываются несколько локомотивов и смешанные составы. Неизвестная масса или тяга отмечается как недоступная и не подменяется нулём.

Оценка допустимой нагрузки использует доступные каталоговые данные и известное состояние сухих/мокрых рельсов. Это справочная оценка для указанного уклона, а не полный расчёт уклонов и тяги вдоль всего маршрута. Для оценки должны быть доступны сведения о нагрузке и погоде.

Удалённое управление локомотивом и сцепками доступно только локальному администратору хоста при соответствующих разрешениях UMM и игры. Оно не является системой автоматического ведения поезда или маневровой работы.

### Заказы

«Заказы» показывает текущие доступные и выполняемые задания игры. Новые задания появляются, изменённые обновляются, а завершённые, брошенные, истёкшие и удалённые исчезают из текущего списка. При наличии данных заказ показывает владельца или назначенного игрока, груз, вагоны, состав, начало, назначение, задачи, оплату, бонусное время и лицензии. Поддерживаемые события заказов записываются в журнал; вкладка «Заказы» не является постоянным архивом удалённых заданий.

При наличии прав и поддержки игры инспектор позволяет принять или отменить задание, назначить игрока и построить маршрут для текущей задачи. Пассажирские маршруты используют обязательные остановки Passenger Jobs; можно добавлять промежуточные точки диспетчера, сохраняя эти остановки. Без Passenger Jobs остаются локации основной игры, без предупреждения об отсутствии необязательного мода.

Для маневровых заданий поддерживается маршрут отдельного подготовленного транспортного этапа, если нужные вагоны и локомотив образуют подтверждённый подходящий состав. Автоматическое извлечение зажатых вагонов, обход состава и многоходовая перестановка вагонов не реализованы.

Приостановка объектов Persistent Jobs отличается от удаления: временно недоступные вагоны или заказы помечаются соответствующим образом, а действия, требующие актуальных данных, остаются недоступными до их восстановления.

### Погода и прогноз

«Погода» показывает текущие условия, доступные значения дождя, облачности, тумана и грозы, влажность рельсов и игровое время. Прогноз содержит текущие и предстоящие периоды, предоставленные игрой, с их временем и продолжительностью. Состояния и иконки берутся из игровых данных; внешний сервис погоды не используется.

Если игровой API не передаёт текущие условия или прогноз, соответствующие данные показываются как недоступные. Погода учитывается в справочной оценке нагрузки для сухих/мокрых рельсов; диспетчерская не меняет погоду игры.

### Игроки и Multiplayer

В одиночной игре используется локальный хост. При совместимом Multiplayer хост может передавать браузерным клиентам подключённых игроков и поддерживаемое реплицируемое состояние сети. Маркер игрока использует переданные положение и направление взгляда, а внутри вагона его позиция привязана к этой машине. Назначение игрока на заказ или маршрут фиксирует ответственность диспетчеризации, но не управляет поездом игрока.

Права браузера и полномочия Multiplayer разделены. Браузер не становится игровым хостом. Несовместимые API отключают связанные команды вместо их локального выполнения. Все web-пользователи одного Web Host получают состояние этого хоста.

### Настройки мода и возврат к значениям по умолчанию

В UMM настройки сгруппированы в Веб-диспетчерскую, Подключение, Права диспетчера и Производительность. Хост управляет удалённым доступом, портом, дополнительным адресом, режимом просмотра, управлением локомотивами владельцем и отображением необнаруженных локомотивов. Клиенты Multiplayer видят полученные значения хоста только для чтения. **Сбор данных за кадр** — личная настройка каждого компьютера: большее значение ускоряет сбор данных, но занимает больше времени игрового кадра.

Сетевые изменения остаются черновиком до нажатия **Применить и перезапустить**. Если изменений нет, эта же кнопка предлагает **Перезапустить веб-сервер**. Остальные рабочие настройки сохраняются при изменении. **Вернуть по умолчанию** сбрасывает настройки мода после подтверждения; у клиента Multiplayer сбрасываются только локальные параметры производительности.

Настройки отображения Web UI сохраняются отдельно в браузере. **Сбросить настройки браузера** возвращает оформление, слои, LOD, цвета, панели, камеру и навигацию, затем перезагружает страницу. Web-профили, пароли и действующий сеанс не удаляются.

### Поиск, сортировка и фильтры

Поиск нормализует русский и английский текст, регистр и пробелы, использует ID, имена и связанные отображаемые имена. Списки поддерживают сортировку, фильтры состояния и активных объектов для выбранного раздела. Длинные списки используют ограниченный виртуальный рендеринг.

### Совместимость

- **Double Track:** Полная совместимость.
- **DV Signals:** Полная совместимость.
- **Passenger Jobs:** Полная совместимость.
- **Persistent Jobs:** Полная совместимость.
- **Multiplayer:** Полная совместимость.

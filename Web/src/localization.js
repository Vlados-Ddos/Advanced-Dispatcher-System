import { readSetting, saveSetting, removeSetting } from "./storage.js";
import { passengerText } from "./localization-passenger.js";
import { presentationText } from "./localization-presentation.js";
import { more } from "./localization-more.js";
const dictionary = {
  lodSettings: ["Level of detail", "Детализация (LOD)"],
  lodSettingsHint: ["100% is the default. Lower values show details farther away; higher values require a closer zoom. These settings do not change game state or icon size.", "100% — стандартные пороги. Меньше — детали видны издалека; больше — требуется приближение. Эти настройки не меняют игру или размер иконок."],
  lodSignals: ["LOD · Main signals", "LOD · Поездные сигналы"],
  lodShunting: ["LOD · Shunting signals", "LOD · Маневровые сигналы"],
  lodIndicators: ["LOD · Signal indicators", "LOD · Дополнительные секции"],
  lodSwitches: ["LOD · Switch icons", "LOD · Значки стрелок"],
  lodRollingStock: ["LOD · Individual rolling stock", "LOD · Отдельные локомотивы и вагоны"],
  lodSigns: ["LOD · Track signs", "LOD · Путевые знаки"],
  lodTrackLabels: ["LOD · Track labels", "LOD · Подписи путей"],
  lodVehicleLabels: ["LOD · Locomotive labels", "LOD · Подписи локомотивов"],
  lodBlockLabels: ["LOD · Block labels", "LOD · Подписи блоков"],
  lodSpeedLabels: ["LOD · Speed labels", "LOD · Подписи скорости"],
  lodTechnical: ["LOD · Technical labels", "LOD · Технические подписи"],
  planningRoute: ["Planning…", "Планирование…"],
  waitingRoute: ["Receiving the confirmed route…", "Получение подтверждённого маршрута…"],
  routeWaypoints: ["Intermediate points", "Промежуточные точки"],
  addWaypoint: ["Add intermediate point", "Добавить промежуточную точку"],
  choosingWaypoint: ["Choose point on map…", "Выберите точку на карте…"],
  removeWaypoint: ["Remove intermediate point", "Удалить промежуточную точку"],
  moveWaypointUp: ["Move point up", "Переместить точку вверх"],
  moveWaypointDown: ["Move point down", "Переместить точку вниз"],
  clearWaypoints: ["Clear intermediate points", "Очистить промежуточные точки"],
  routeWaypointInvalid: ["This track cannot be used as an intermediate point", "Этот путь нельзя использовать как промежуточную точку"],
  signalTooltip: ["Signal preview on hover", "Показание светофора при наведении"],
  lastKnownSignal: ["Last received indication", "Последнее полученное показание"],
  destinationLegend: ["Destination colors", "Цвета назначений"],
  routeSequenceTab: ["Route sequence", "Последовательность маршрута"],
  sequencePrevious: ["Previous segments", "Предыдущие участки"],
  sequenceNext: ["Next segments", "Следующие участки"],
  sequencePage: ["Page", "Страница"],
  relatedTrackObjects: ["Related blocks and routes", "Связанные блоки и маршруты"],
  relatedBlocks: ["Related blocks", "Связанные блоки"],
  relatedRoutes: ["Related routes", "Связанные маршруты"],
  noRelatedBlocks: ["No blocks use this track.", "С этим путём нет связанных блоков."],
  noRelatedRoutes: ["No active routes use this track.", "Активных маршрутов для этого пути нет."],
  selectTrackHint: ["Alt-click selects the track beneath markers. Route planning selects tracks directly.", "Alt + щелчок выбирает путь под маркерами. При планировании маршрута пути выбираются напрямую."],
  rollingStockGroup: ["Coupled vehicles", "Сцепленная группа"],
  caboose: ["Caboose", "Служебный вагон"],
  tender: ["Tender", "Тендер"],
  slug: ["Traction booster", "Тяговая секция"],

  jobWagons: ["Job wagons", "Вагоны заказа"],
  jobWagonsEmpty: [
    "No wagons are recorded for this job.",
    "Для этого заказа вагоны не указаны.",
  ],
  jobWagonUnavailable: ["Wagon unavailable", "Вагон недоступен"],
  focusJobWagon: [
    "Select wagon and focus the map",
    "Выбрать вагон и показать на карте",
  ],
  multipleLocomotives: [
    "Train with multiple locomotives",
    "Состав с несколькими локомотивами",
  ],
  singleVehicle: ["Separate vehicle", "Отдельная единица"],

  sort_player: ["Group by player", "По игрокам"],
  routePreviewTime: [
    "Preview — not yet created",
    "Предпросмотр — маршрут ещё не создан",
  ],
  eventTimeUnavailable: ["Time not recorded", "Время не записано"],
  eventSourceUnavailable: ["Source not recorded", "Источник не записан"],
  eventActorUnavailable: ["Initiator not recorded", "Инициатор не записан"],
  eventObjectUnavailable: [
    "The linked object is unavailable or no exact link was recorded.",
    "Связанный объект недоступен или точная связь не была записана.",
  ],
  goToEventObject: ["Go to event object", "Перейти к объекту события"],
  eventSourceDispatch: [
    "Advanced Dispatcher System",
    "Advanced Dispatcher System",
  ],

  occupancyScale: ["Occupied track line width", "Толщина линии занятого пути"],
  branchColor: ["Turnout branch colour", "Цвет ветви стрелочного перевода"],
  temporaryDraftHint: [
    "Choose an aspect and apply it below. The temporary mode and aspect will be sent together; the current mode above is the confirmed game state.",
    "Выберите показание и примените его ниже. Временный режим и показание отправятся вместе; текущее состояние игры показано выше.",
  ],
  routeIndicatorAutomatic: [
    "Depending on the signal pack, Track Set To displays the connected block or its fixed placement track. Route-dependent indications follow actual turnout settings. This display has no independent manual setter.",
    "В зависимости от набора сигналов указатель показывает связанный блок либо путь установки головки. Маршрутное показание следует фактическому положению стрелок. Отдельной ручной команды для этого указателя нет.",
  ],
  departureIndicatorAutomatic: [
    "Departure Allowed is calculated from active jobs of cars that stopped on the assigned station track, their destination and the signal pack’s conditions. It is dark when the head is off. DV Signals exposes no independent override for this indicator.",
    "Разрешение отправления вычисляется по активным заданиям вагонов, останавливавшихся на назначенном станционном пути, их назначению и условиям набора сигналов. При выключенной головке индикатор погашен. DV Signals не предоставляет для него отдельной ручной установки.",
  ],
  jobActorUnknown: ["Player not identified", "Игрок не определён"],
  eventPlayerUnavailable: [
    "This player has disconnected or is no longer available.",
    "Этот игрок отключился или больше недоступен.",
  ],

  signalReservationDuration: [
    "Reservation duration, seconds (0 = unlimited)",
    "Срок резерва, секунд (0 = бессрочно)",
  ],
  signalReservationDurationHint: [
    "This duration applies only to the reservation. Native manual aspects have no timed expiry.",
    "Срок относится только к резервированию. У ручного показания в игре нет таймера сброса.",
  ],
  reservationRemaining: ["Reservation time remaining", "Остаток срока резерва"],
  reservationUnlimited: ["No time limit", "Без ограничения времени"],
  storedSignalOverride: ["Stored override", "Заданное вручную показание"],
  signalCannotReserve: [
    "This head does not support its own reservation.",
    "Эта головка не поддерживает отдельное резервирование.",
  ],
  signalModeHint_Automatic: [
    "The game selects the aspect. Select a manual mode to apply an override.",
    "Показание выбирает игра. Для ручной установки выберите соответствующий режим.",
  ],
  signalModeHint_TempOverride: [
    "One native evaluation: the first matching aspect (including the override itself) returns the mode to automatic. More restrictive aspects take priority. Reservation duration is unrelated.",
    "Одна игровая оценка: первое подходящее показание, включая само заданное, возвращает автоматический режим. Более строгое показание имеет приоритет. Срок резерва на это не влияет.",
  ],
  signalModeHint_SemiManual: [
    "The override is stored, but a more restrictive automatic aspect takes priority.",
    "Ручное показание сохраняется, но более строгое автоматическое показание имеет приоритет.",
  ],
  signalModeHint_FullManual: [
    "The game applies the stored manual aspect until the override or mode changes.",
    "Игра применяет заданное показание до изменения установки или режима.",
  ],
  SIGNAL_STATE_NOT_CONFIRMED: [
    "The game did not confirm the requested signal state.",
    "Игра не подтвердила запрошенное состояние сигнала.",
  ],
  SIGNAL_AUTOMATIC_MODE: [
    "Automatic mode does not apply a manual aspect.",
    "В автоматическом режиме ручное показание не применяется.",
  ],
  SIGNAL_MODE_RETURNED_AUTO: [
    "DV Signals evaluated the temporary setting and returned to automatic operation as designed. The panel shows the confirmed current aspect.",
    "DV Signals обработал временную установку и штатно вернул автоматический режим. В панели показано подтверждённое текущее показание.",
  ],
  SIGNAL_OVERRIDE_RESTRICTED: [
    "Override stored; the current aspect remains restricted by the game's conditions.",
    "Установка сохранена; текущее показание ограничено условиями игры.",
  ],
  SIGNAL_RESERVATION_UNCERTAIN: [
    "The previous reservation request has no confirmed outcome. Wait for its native response or reconnect the integration.",
    "Результат предыдущего запроса резерва не подтверждён. Дождитесь ответа игры или переподключения интеграции.",
  ],
  branchScale: ["Turnout branch emphasis", "Толщина ветвей стрелок"],
  indicatorScale: [
    "Supplementary signal indicators",
    "Размер указателей светофоров",
  ],
  signScale: ["Railway sign size", "Размер железнодорожных знаков"],
  branchLegend: ["Selected branch", "Выбранная ветвь"],
  selectedLegend: ["Selected object", "Выбранный объект"],
  reservedLegend: ["Reserved", "Зарезервировано"],
  trackCount_one: ["track", "путь"],
  trackCount_few: ["tracks", "пути"],
  trackCount_many: ["tracks", "путей"],
  trackCount_other: ["tracks", "пути"],
  signalCount_one: ["signal", "сигнал"],
  signalCount_few: ["signals", "сигнала"],
  signalCount_many: ["signals", "сигналов"],
  signalCount_other: ["signals", "сигнала"],
  mapTitle: ["Railway map", "Железнодорожная карта"],
  vehicleModel: ["Vehicle model", "Модель техники"],
  modelUnavailable: ["Model unavailable", "Модель неизвестна"],
  consistCargo: ["Consist cargo", "Груз состава"],
  destinationUnknown: ["Destination unknown", "Назначение неизвестно"],
  retryReleaseRoute: ["Retry reservation cleanup", "Повторить снятие резерва"],
  settingsVolatile: [
    "The browser cannot save these preferences. Changes still apply in this tab but may be lost after closing it.",
    "Браузер не может сохранить настройки. Изменения действуют в этой вкладке, но могут потеряться после её закрытия.",
  ],
  accountRecovery: [
    "The account file is damaged. Its original bytes were backed up. Open Settings as the local host owner to recreate accounts; password sign-in is disabled until repair.",
    "Файл учётных записей повреждён; исходный файл сохранён в резервной копии. Владелец хоста может восстановить записи в настройках. Вход по паролю до восстановления недоступен.",
  ],
  ...more,
  ...presentationText,
  ...passengerText,
  TURNTABLE_AREA_UNLOADED: [
    "Turntable components are not loaded. Bring the host player to the area and retry.",
    "Компоненты круга не загружены. Приблизьте игрока хоста к кругу и повторите команду.",
  ],
  TURNTABLE_CONTROL_UNAVAILABLE: [
    "Turntable controls are not initialized. Visit the area and retry.",
    "Управление кругом не инициализировано. Посетите этот участок и повторите команду.",
  ],
  TURNTABLE_MANEUVER_REQUIRED: [
    "No continuous route: this turntable connection requires a separate manual train manoeuvre.",
    "Невозможно построить непрерывный маршрут: требуется отдельный манёвр и ручное управление поворотным кругом.",
  ],
  status: ["Status", "Состояние"],
  switchBranches: ["Switch branch highlights", "Подсветка ветвей стрелок"],
  switchIcons: ["Switch icons", "Иконки стрелок"],
  turntables: ["Turntables", "Поворотные круги"],
  tableAngle: ["Current angle", "Текущий угол"],
  tableMoving: ["Rotating", "Переводится"],
  setTurntable: ["Align turntable", "Установить круг"],
  tableSafetyHint: [
    "Remote rotation requires an empty bridge and clear approaches. Non-opposite exits require a separate train manoeuvre.",
    "Дистанционный перевод требует свободного моста и подходов. Для непротивоположных выходов нужен отдельный манёвр состава.",
  ],
  TURNTABLE_OCCUPIED: [
    "Turntable or clearance area occupied",
    "Круг или габаритная зона заняты",
  ],
  TURNTABLE_APPROACHING: [
    "Rolling stock approaching turntable",
    "Подвижной состав приближается к кругу",
  ],
  TURNTABLE_BUSY: [
    "Turntable is moving or controlled locally",
    "Круг движется или управляется на месте",
  ],
  TURNTABLE_CHANGED_EXTERNALLY: [
    "Turntable changed outside dispatcher",
    "Круг переведён вне диспетчерской",
  ],
  TURNTABLE_NOT_CONNECTED: [
    "Game did not confirm the connection",
    "Игра не подтвердила соединение",
  ],
  TURNTABLE_MISALIGNED: ["Turntable needs alignment", "Круг требует перевода"],
  TURNTABLE_UNKNOWN: [
    "Turntable state unavailable",
    "Состояние круга недоступно",
  ],
  cargoEmpty: ["Empty", "Без груза"],
  cargoUnavailable: ["Cargo data unavailable", "Данные о грузе недоступны"],
  cargoPartial: [
    "Some cars have no cargo data",
    "По части вагонов нет данных о грузе",
  ],
  COUPLING_UNAVAILABLE: [
    "No free coupler in range",
    "Нет свободной сцепки в пределах досягаемости",
  ],
  resizeNavigation: ["Resize navigation", "Ширина панели разделов"],
  resizeList: ["Resize list panel", "Размер панели списка"],
  resizeDetails: ["Resize details panel", "Размер панели сведений"],
  resizewidth: ["width", "ширина"],
  resizeHint: [
    "Drag a side edge. Left/right keys resize; double-click or Home resets.",
    "Тяните боковой край. Влево/вправо меняют ширину; двойной щелчок или Home сбрасывает её.",
  ],
  resetPanels: ["Reset panel sizes", "Сбросить размеры панелей"],
  connecting: ["Connecting", "Подключение"],
  connected: ["Live", "На связи"],
  reconnecting: ["Reconnecting", "Переподключение"],
  offline: ["Offline", "Нет связи"],
  loading: ["Loading railway", "Загрузка сети"],
  ready: ["Ready", "Готово"],
  disconnected: ["Game disconnected", "Нет связи с игрой"],
  unloaded: ["World unloaded", "Мир выгружен"],
  logout: ["Sign out", "Выйти"],
  network: ["NETWORK OVERVIEW", "ОБЗОР СЕТИ"],
  dispatch: ["Dispatch console", "Диспетчерская"],
  search: ["Locomotives, wagons, tracks…", "Локомотивы, вагоны, пути…"],
  activeOnly: ["Active", "Активные"],
  advisoryShort: ["Route reservations", "Резервации маршрутов"],
  advisory: [
    "Planning aligns the route. Normal reservations allow manual switches; protected reservations lock them until release.",
    "Планирование выравнивает маршрут. Обычный резерв допускает ручной перевод, защищённый блокирует стрелки до снятия резерва.",
  ],
  fit: ["Fit network", "Вся сеть"],
  playerHome: ["To players", "К игрокам"],
  follow: ["Follow", "Следить"],
  layers: ["Layers", "Слои"],
  theme: ["Theme", "Тема"],
  metrics: ["Metrics", "Метрики"],
  waitingWorld: ["Waiting for the railway", "Ожидание железной дороги"],
  loadWorld: [
    "Load a game to see live tracks and trains.",
    "Загрузите игру — здесь появятся пути и подвижной состав.",
  ],
  free: ["Clear", "Свободно"],
  occupied: ["Occupied", "Занято"],
  planned: ["Plan", "План"],
  tracks: ["Tracks", "Пути"],
  trains: ["Consists", "Составы"],
  cars: ["Cars", "Вагоны"],
  signals: ["Signals", "Сигналы"],
  switches: ["Switches", "Стрелки"],
  blocks: ["Track blocks", "Путевые блоки"],
  players: ["Players", "Игроки"],
  routes: ["Routes", "Маршруты"],
  jobs: ["Orders", "Заказы"],
  log: ["Events", "События"],
  settings: ["Settings", "Настройки"],
  labels: ["Labels", "Подписи"],
  username: ["Web account name", "Имя веб-учётной записи"],
  password: ["Password", "Пароль"],
  signIn: ["Sign in", "Войти"],
  signInHint: [
    "Use the web account created by the host to view or control this dispatch console. Your in-game name does not grant access.",
    "Для просмотра и управления диспетчерской нужна веб-учётная запись, созданная хостом. Игровой ник не даёт прав доступа.",
  ],
  loginHelp: [
    "Hosting the game? Open dispatcher in this mod's Unity Mod Manager settings for local owner access, without a password. Other users receive an account name and password from the host.",
    "Вы хост игры? Нажмите «Открыть диспетчерскую» в настройках мода в Unity Mod Manager — локальный вход владельца не требует пароля. Остальным пользователям имя и пароль выдаёт хост.",
  ],
  loginFailed: [
    "Unable to sign in. Check your credentials.",
    "Не удалось войти. Проверьте имя и пароль.",
  ],
  empty: ["No objects to display", "Нет объектов для отображения"],
  noMatch: ["No matches", "Ничего не найдено"],
  speed: ["Speed", "Скорость"],
  length: ["Length", "Длина"],
  direction: ["Direction", "Направление"],
  forward: ["Forward", "Вперёд"],
  reverse: ["Reverse", "Назад"],
  stopped: ["Stopped", "Стоит"],
  unknown: ["Unknown", "Неизвестно"],
  track: ["Track", "Путь"],
  branch: ["Branch", "Ветвь"],
  aspect: ["Aspect", "Показание"],
  mode: ["Mode", "Режим"],
  source: ["Source", "Источник"],
  occupation: ["Occupancy", "Занятость"],
  reservation: ["Reservation", "Резервирование"],
  reserved: ["Reserved", "Зарезервировано"],
  none: ["None", "Нет"],
  from: ["From", "Откуда"],
  to: ["To", "Куда"],
  startRoute: ["Route from here", "Начало маршрута"],
  endRoute: ["Route to here", "Конец маршрута"],
  preview: ["Preview", "Предпросмотр"],
  plan: ["Plan route", "Запланировать"],
  applyRoute: ["Align route", "Выставить маршрут"],
  cancelRoute: ["Cancel route", "Отменить маршрут"],
  clear: ["Clear", "Очистить"],
  destination: ["Destination", "Назначение"],
  job: ["Job", "Задание"],
  mass: ["Current mass", "Текущая масса"],
  payment: ["Base payment", "Базовая оплата"],
  licenses: ["Licences", "Лицензии"],
  shunting: ["Shunting allowed", "Маневры разрешены"],
  reserveSignal: ["Reserve signal", "Зарезервировать сигнал"],
  cancelSignal: ["Release signal reservation", "Снять резерв сигнала"],
  duration: [
    "Duration, seconds (0 = indefinite)",
    "Срок, секунд (0 = бессрочно)",
  ],
  setAspect: ["Set override", "Задать показание"],
  off: ["Off", "Погашен"],
  throttle: ["Throttle", "Тяга"],
  trainBrake: ["Train brake", "Поездной тормоз"],
  independentBrake: ["Independent brake", "Независимый тормоз"],
  reverser: ["Reverser", "Реверсор"],
  brakePipe: ["Brake pipe", "Тормозная магистраль"],
  couple: ["Couple", "Сцепить"],
  uncouple: ["Uncouple", "Расцепить"],
  derailed: ["Derailed", "Сход с рельсов"],
  slipping: ["Wheel slip", "Боксование"],
  locate: ["Locate", "Показать"],
  showCars: ["Show cars", "Показать вагоны"],
  readOnly: ["Read-only", "Только просмотр"],
  hostOnly: [
    "Connect to the game host to control the railway.",
    "Для управления подключитесь к игровому хосту.",
  ],
  demo: [
    "DEMO · Synthetic benchmark data, not your game",
    "ДЕМО · Синтетические данные теста, не ваша игра",
  ],
  stale: [
    "Game data is stale. Controls are unavailable.",
    "Данные игры устарели. Управление недоступно.",
  ],
  loadedWorld: ["Live railway", "Железнодорожная сеть"],
  accounts: ["Accounts & permissions", "Учётные записи и права"],
  accountHint: [
    "Host administration — this form creates or edits access for other users; it does not sign you in. Names use letters, digits, _ or -. Passwords need 10–128 characters. Saving an existing account replaces its password and ends its sessions.",
    "Управление доступом: эта форма создаёт или изменяет учётные записи, а не выполняет вход. В имени допустимы буквы, цифры, _ и -. Пароль — 10–128 символов. Сохранение существующей записи заменяет пароль и завершает её сеансы.",
  ],
  role: ["Role", "Роль"],
  viewer: ["Viewer", "Наблюдатель"],
  dispatcher: ["Dispatcher", "Диспетчер"],
  admin: ["Administrator", "Администратор"],
  saveAccount: ["Save account", "Сохранить запись"],
  accountSaved: [
    "Account saved. Previous sessions were revoked.",
    "Запись сохранена. Старые сеансы этой записи завершены.",
  ],
  accountFailed: [
    "Could not save the account.",
    "Не удалось сохранить запись.",
  ],
  themeDark: ["Dark", "Тёмная"],
  themeLight: ["Light", "Светлая"],
  language: ["Language", "Язык"],
  colorMode: ["Car colours", "Цвет вагонов"],
  colorJobType: ["By job type", "По типу задания"],
  colorDestination: ["By destination", "По назначению"],
  telemetry: ["Diagnostics", "Диагностика"],
  capture: ["Game capture", "Чтение игры"],
  renderer: ["Renderer", "Отрисовка"],
  clients: ["Browsers", "Браузеры"],
  received: ["Received", "Получено"],
  age: ["State age", "Возраст данных"],
  fps: ["Browser FPS", "FPS браузера"],
  eta: ["ETA at current speed", "ETA при текущей скорости"],
  selectEnd: ["Select the destination track", "Выберите конечный путь"],
  routeLength: ["Route length", "Длина маршрута"],
  noRoute: ["No connected route found", "Связный маршрут не найден"],
  advisoryPlan: ["Advisory plan", "Диспетчерский план"],
  nativeBlock: ["DV Signals block", "Путевой блок DV Signals"],
  section: ["Dispatch section", "Диспетчерская секция"],
  ms: ["ms", "мс"],
  meters: ["m", "м"],
  kmh: ["km/h", "км/ч"],
  tons: ["t", "т"],
  seconds: ["s", "с"],
  yes: ["Yes", "Да"],
  no: ["No", "Нет"],
  Automatic: ["Automatic", "Автоматический"],
  TempOverride: ["Temporary override", "Временная установка"],
  SemiManual: ["Semi-manual", "Полуручной"],
  FullManual: ["Manual", "Ручной"],
  applied: ["Command applied", "Команда выполнена"],
  accepted: ["Command queued", "Команда в очереди"],
  outcomeUnknown: [
    "Command outcome is unknown; inspect the state before retrying.",
    "Результат неизвестен; проверьте состояние перед повтором.",
  ],
  warning: ["Warning", "Предупреждение"],
  aligned: ["Switches aligned", "Стрелки установлены"],
  plannedStatus: ["Planned", "Запланирован"],
  signalUnavailable: [
    "Signals integration is unavailable",
    "Интеграция Signals недоступна",
  ],
  host: ["Host", "Хост"],
  client: ["Client", "Клиент"],
  singleplayer: ["Singleplayer", "Одиночная игра"],
  close: ["Close", "Закрыть"],
  settingsRestart: [
    "LAN, HTTPS and the port are configured in Unity Mod Manager.",
    "LAN, HTTPS и порт настраиваются в Unity Mod Manager.",
  ],
  findTrack: ["Find track", "Найти путь"],
  rescan: ["Refresh topology", "Обновить сеть"],
  exportLog: ["Export event log", "Сохранить журнал"],
  AUTH_REQUIRED: ["Sign in first", "Сначала войдите"],
  FORBIDDEN: ["You do not have permission", "Недостаточно прав"],
  HOST_REQUIRED: [
    "The game host must execute this command",
    "Команда должна выполняться хостом",
  ],
  WORLD_NOT_READY: ["The game world is not ready", "Игровой мир ещё не готов"],
  STALE_REVISION: [
    "State changed; review and try again",
    "Состояние изменилось; проверьте его и повторите",
  ],
  STALE_EPOCH: ["The game session changed", "Игровой сеанс изменился"],
  TOPOLOGY_CHANGED: [
    "Railway topology changed",
    "Железнодорожная сеть изменилась",
  ],
  INVALID_BRANCH: ["Invalid switch branch", "Неверная ветвь стрелки"],
  INVALID_VALUE: ["Invalid value", "Недопустимое значение"],
  INVALID_COMMAND: ["Invalid command", "Неверная команда"],
  NOT_FOUND: ["Object no longer exists", "Объект больше не существует"],
  NOT_CONTROLLABLE: [
    "Locomotive control is unavailable",
    "Управление локомотивом недоступно",
  ],
  CAPABILITY_UNAVAILABLE: [
    "Integration is unavailable",
    "Интеграция недоступна",
  ],
  RATE_LIMITED: [
    "Too many requests; wait a moment",
    "Слишком много запросов; подождите",
  ],
  QUEUE_FULL: ["Command queue is busy", "Очередь команд заполнена"],
  GAME_DISCONNECTED: [
    "Connection to the game was lost",
    "Связь с игрой потеряна",
  ],
  COMMAND_TIMEOUT: [
    "Command timed out; outcome unknown",
    "Таймаут команды; результат неизвестен",
  ],
  COMMAND_EXPIRED: ["Command expired", "Срок команды истёк"],
  COMMAND_ID_REUSED: [
    "Command ID was reused with different data",
    "ID команды повторён с другими данными",
  ],
  COMMAND_PENDING: [
    "A request for this signal is already pending",
    "Для этого сигнала уже выполняется запрос",
  ],
  COMMAND_FAILED: [
    "The game could not execute the command",
    "Игра не смогла выполнить команду",
  ],
  NO_ROUTE: [
    "No route through the current connections in the train’s direction. Choose another destination or reverse the train.",
    "Нет маршрута по текущим соединениям в направлении движения поезда. Выберите другой путь или измените направление движения.",
  ],
  ROUTE_PARTIAL: [
    "Some route commands could not be applied; inspect the indicated object",
    "Часть команд маршрута не выполнена; проверьте указанный объект",
  ],
  ROUTE_CANCELLED: ["Route plan cancelled", "План маршрута отменён"],
  RESERVATION_CONFLICT: [
    "DV Signals could not reserve this block",
    "DV Signals не смог зарезервировать блок",
  ],
  TRACK_OCCUPIED: [
    "The route or switch is occupied",
    "Маршрут или стрелка заняты",
  ],
  OCCUPANCY_UNKNOWN: [
    "Some occupancy information is unknown",
    "Занятость части путей неизвестна",
  ],
  ROUTE_OVERLAP: [
    "This plan overlaps another plan",
    "План пересекается с другим планом",
  ],
  SIGNAL_RESERVED: [
    "DV Signals has reserved part of this route",
    "DV Signals зарезервировал часть маршрута",
  ],
  SWITCH_MISALIGNED: [
    "Some switches do not match the route",
    "Положение части стрелок не соответствует маршруту",
  ],
  SWITCH_CHANGED_EXTERNALLY: [
    "Another system changed the switch",
    "Другая система изменила стрелку",
  ],
  SHARED_LOCO_CONTROL: [
    "Locomotive controls are shared with players",
    "Органы управления локомотивом общие с игроками",
  ],
  SIGNALS_SHARED_REQUEST: [
    "DV Signals reservations are shared with game controls",
    "Резервы DV Signals общие с игровыми органами управления",
  ],
  APPROACHING_TRAIN: [
    "A moving train is approaching this switch",
    "К стрелке приближается поезд",
  ],
  switchChanged: ["Switch changed", "Стрелка переключена"],
  topologyChanged: ["Railway updated", "Сеть обновлена"],
  routePlanned: ["Route planned", "Маршрут запланирован"],
  routeCancelled: ["Plan cancelled", "План отменён"],
  setSwitch: ["Switch command", "Команда стрелке"],
  loco: ["Locomotive command", "Команда локомотиву"],
  setSignalMode: ["Signal mode changed", "Изменён режим сигнала"],
  setSignalAspect: ["Signal override changed", "Изменено перекрытие сигнала"],
  setShunting: ["Shunting setting changed", "Изменён маневровый режим"],
  cancelSignalReservation: ["Reservation cleared", "Резерв снят"],
  planRoute: ["Route planned", "Маршрут запланирован"],
  eventTime: ["Time", "Время"],
  routeOwner: ["Dispatcher", "Диспетчер"],
  reservationMode: ["Reservation mode", "Режим резервации"],
  reservation_none: ["No reservation", "Без резервации"],
  reservation_normal: ["Normal", "Обычная"],
  reservation_protected: ["Protected", "Защищённая"],
  reserve_normal: ["Reserve normally", "Обычная резервация"],
  reserve_protected: ["Protected reservation", "Защищённая резервация"],
  normalRouteHint: [
    "Manual switches remain available. A changed route releases its reservation and reports the mismatch.",
    "Ручной перевод доступен. При изменении пути резерв снимается и появляется предупреждение о несовпадении.",
  ],
  protectedRouteHint: [
    "Route switches are locked in the game and at the multiplayer host until the reservation is released.",
    "Стрелки маршрута заблокированы в игре и на хосте Multiplayer до снятия резерва.",
  ],
  activeRoutes: ["Active routes", "Активные маршруты"],
  routeWarnings: ["With warnings", "С предупреждениями"],
  reservedRoutes: ["Reserved routes", "Зарезервированные"],
  routeHistory: ["Route history", "История маршрутов"],
  routeHistoryHint: [
    "Archived plan. Linked infrastructure details show its current state.",
    "Архивный план. Сведения по связанным объектам показывают их текущее состояние.",
  ],
  warningHistory: ["Resolved warnings", "Устранённые предупреждения"],
  warningActive: ["Active", "Активно"],
  warningResolved: ["Resolved / historical", "Устранено / история"],
  resolvedAt: ["Resolved at", "Устранено"],
  reason: ["Reason", "Причина"],
  preparing: ["Aligning and verifying", "Перевод и проверка"],
  completed: ["Completed", "Завершён"],
  cancelled: ["Cancelled", "Отменён"],
  interrupted: ["Interrupted", "Прерван"],
  released: ["Released", "Снят"],
  releaseFailed: [
    "Release not fully confirmed",
    "Снятие подтверждено не полностью",
  ],
  SIGNALS_RELEASE_FAILED: [
    "Switch locks were removed, but DV Signals reservation release failed; check its actual state",
    "Блокировка стрелок снята, но снять резерв DV Signals не удалось; проверьте его фактическое состояние",
  ],
  completeRoute: ["Complete and release", "Завершить и освободить"],
  releaseRoute: ["Release reservation", "Снять резервацию"],
  routeCompleted: ["Route completed", "Маршрут завершён"],
  routeInterrupted: ["Route interrupted", "Маршрут прерван"],
  routeWarning: ["Route warning appeared", "Предупреждение маршрута"],
  routeWarningResolved: [
    "Route warning resolved",
    "Предупреждение маршрута устранено",
  ],
  SWITCH_LOCKED: [
    "Switch locked by a protected route",
    "Стрелка заблокирована защищённым маршрутом",
  ],
  SWITCH_OCCUPIED: [
    "Rolling stock is on the switch; moving it is unsafe",
    "На стрелке находится подвижной состав; перевод небезопасен",
  ],
  ROUTE_RESERVED: [
    "Another reservation owns this track or switch",
    "Путь или стрелка уже принадлежат другой резервации",
  ],
  ROUTE_ALREADY_RESERVED: [
    "Release the current reservation before changing its mode",
    "Перед изменением режима снимите действующую резервацию",
  ],
  ROUTE_ENDED: [
    "This route has ended; create a new plan",
    "Маршрут завершён; создайте новый план",
  ],
  INVALID_ROUTE: ["Invalid route data", "Некорректные данные маршрута"],
  INVALID_RESERVATION: [
    "Invalid reservation mode",
    "Некорректный режим резервации",
  ],
  PROTECTION_UNAVAILABLE: [
    "The installed multiplayer API cannot enforce switch protection",
    "Для установленной версии Multiplayer не удалось включить серверную блокировку стрелок",
  ],
  SIGNALS_SYNC_UNAVAILABLE: [
    "DV Signals multiplayer synchronization is unavailable",
    "Синхронизация резервов DV Signals с Multiplayer недоступна",
  ],
  SIGNAL_PATH_CHANGED: [
    "The native signal block changed; recalculate the route",
    "Путевой блок сигнала изменился; пересчитайте маршрут",
  ],
  RESERVATION_LOST: [
    "Reservation was removed or the game connection was reset",
    "Резервация снята извне или соединение с игрой сброшено",
  ],
  TRAIN_CHANGED: [
    "Train membership changed or a car was removed/derailed; reservation released",
    "Состав изменился, вагон удалён или сошёл с рельсов; резерв снят",
  ],
  showRouteFocus: [
    "Show route on map without icons",
    "Показать маршрут на карте без иконок",
  ],
  exitRouteFocus: ["Return to normal map", "Вернуть обычный вид карты"],
  routeFocusActive: [
    "Route view · icons hidden",
    "Обзор маршрута · иконки скрыты",
  ],
  interfaceSettings: ["Interface and panels", "Интерфейс и панели"],
  signalSettings: ["Signal appearance", "Вид сигналов"],
  mapSettings: ["Map and rolling stock", "Карта и подвижной состав"],
  dispatchSettings: ["Dispatching and event log", "Диспетчеризация и журнал"],
  accessSettings: ["Web access", "Доступ к диспетчерской"],
  webAccessExplanation: [
    "These web accounts protect access to the host's dispatcher. They are separate from game players and multiplayer nicknames.",
    "Веб-учётные записи определяют доступ к диспетчерской хоста. Они не связаны с игровыми персонажами и никами Multiplayer.",
  ],
  ownerAccessHelp: [
    "Local host owner: infrastructure, account administration, and rolling stock only when enabled in the game's mod settings.",
    "Локальный владелец: инфраструктура, управление доступом и подвижной состав, если это разрешено в игровых настройках мода.",
  ],
  dispatcherAccessHelp: [
    "Dispatcher: inspect the railway and control infrastructure and routes. Rolling stock control is reserved for the host owner.",
    "Диспетчер: просмотр сети, управление инфраструктурой и маршрутами. Управление подвижным составом доступно только владельцу хоста.",
  ],
  viewerAccessHelp: [
    "Viewer: open any object's details and use visual map settings. Game commands are unavailable.",
    "Наблюдатель: сведения об объектах и визуальные настройки карты. Игровые команды недоступны.",
  ],
  localOwner: ["Host owner", "Владелец хоста"],
  existingAccounts: [
    "Create or edit web access",
    "Создать или изменить доступ",
  ],
  newAccount: ["New web account", "Новая веб-учётная запись"],
  accountsUnavailable: [
    "Unable to load accounts",
    "Не удалось загрузить учётные записи",
  ],
  SERVER_UNAVAILABLE: [
    "Cannot reach the dispatcher server. Check that the host is running.",
    "Не удалось связаться с диспетчерской. Проверьте, запущен ли сервер хоста.",
  ],
  LOGOUT_FAILED: [
    "Disconnected locally, but the server did not confirm sign-out. Reconnect and sign out again to revoke that session.",
    "Локальное подключение закрыто, но сервер не подтвердил выход. После восстановления связи повторите выход для завершения сеанса на сервере.",
  ],
  failed: ["Not completed", "Не выполнено"],
  Clear: ["Proceed", "Движение разрешено"],
  Stop: ["Stop", "Стой"],
  throughTrack: ["Through track", "Прямой путь"],
  divergingTrack: ["Diverging track", "Боковой путь"],
  turntableName: ["Turntable", "Поворотный круг"],
  layerRail: ["Railway tracks", "Железнодорожные пути"],
  layerAllTracks: ["All tracks", "Все пути"],
  layerJunctions: ["Turnouts", "Стрелочные переводы"],
  layerMainSignals: ["Main and other signals", "Поездные и прочие"],
  layerInfrastructure: ["Infrastructure", "Инфраструктура"],
  layerStations: ["Other stations", "Остальные станции"],
  layerIndustries: ["Loading stations", "Погрузочные станции"],
  layerTraffic: ["Rolling stock and players", "Подвижной состав и игроки"],
  layerRollingStock: ["Rolling stock", "Подвижной состав"],
  layerDispatch: ["Dispatching", "Диспетчеризация"],
  layerOccupation: [
    "Occupied tracks and blocks",
    "Занятость путей и путевых блоков",
  ],
  layerSpeed: ["Speeds and signs", "Скорости и знаки"],
  layerLabels: ["Labels and details", "Подписи и сведения"],
  layerObjectNames: ["Object names", "Названия объектов"],
  layerWholeGroup: ["All layers in group", "Вся группа"],
  layerSettingsHint: [
    "Visibility is saved in this browser. Empty layers keep their settings when objects load. Loading stations have actual warehouse equipment; other stations are shown separately.",
    "Видимость сохраняется в этом браузере. Пустой слой сохраняет настройки до появления объектов. Погрузочные станции определяются по наличию погрузочных устройств; остальные показываются отдельно.",
  ],
  layerFocusHint: [
    "Route overview temporarily hides icons. These switches set the normal map view and apply when you leave overview.",
    "Обзор маршрута временно скрывает иконки. Переключатели настраивают обычный вид карты — он вернётся после выхода из обзора.",
  ],
};
let locale =
  readSetting("ads.hostLanguage") ||
  (navigator.language.startsWith("ru") ? "ru" : "en");
if (!["en", "ru"].includes(locale)) locale = "en";
// Bounded diagnostic inventory for tests/devtools; unknown wire enums never
// become user-facing localization keys. Official names use displayName.
export const missingLocalizationKeys = new Set();
export function t(key) {
  const entry = Object.hasOwn(dictionary, key) ? dictionary[key] : null;
  if (entry) return entry[locale === "ru" ? 1 : 0] || entry[0];
  if (!key) return "—";
  if (missingLocalizationKeys.size < 256)
    missingLocalizationKeys.add(String(key));
  return dictionary.valueUnavailable[locale === "ru" ? 1 : 0];
}
export function language() {
  return locale;
}
export function displayName(value) {
  const name = String(value ?? "");
  return name
    .replace(/\[track (through|diverging)\]/gi, (_, type) =>
      t(type.toLowerCase() === "through" ? "throughTrack" : "divergingTrack"),
    )
    .replace(/^Turntable /, t("turntableName") + " ");
}
export function setLanguage(value) {
  locale = ["en", "ru"].includes(value) ? value : "en";
  saveSetting("ads.hostLanguage", locale);
  removeSetting("ads.language");
  translate();
  window.dispatchEvent(new Event("ads-language"));
}
export function translate(root = document) {
  document.documentElement.lang = locale;
  root
    .querySelectorAll("[data-i18n]")
    .forEach((el) => (el.textContent = t(el.dataset.i18n)));
  root
    .querySelectorAll("[data-aria]")
    .forEach((el) => el.setAttribute("aria-label", t(el.dataset.aria)));
  root
    .querySelectorAll("[data-placeholder]")
    .forEach((el) => (el.placeholder = t(el.dataset.placeholder)));
}
const pluralFormats = new Map();
export function countText(value, key) {
  if (!pluralFormats.has(locale))
    pluralFormats.set(locale, new Intl.PluralRules(locale));
  return (
    number(value) + " " + t(key + "_" + pluralFormats.get(locale).select(value))
  );
}
const numberFormats = new Map();
const collators = new Map();
export function compareNames(a, b) {
  let c = collators.get(locale);
  if (!c) {
    c = new Intl.Collator(locale, { numeric: true });
    collators.set(locale, c);
  }
  return c.compare(a, b);
}
export function number(value, digits = 0) {
  const key = locale + ":" + digits;
  let format = numberFormats.get(key);
  if (!format) {
    format = new Intl.NumberFormat(locale, { maximumFractionDigits: digits });
    numberFormats.set(key, format);
  }
  return Number.isFinite(Number(value)) ? format.format(value) : "—";
}
export { dictionary };

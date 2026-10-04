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
  calculatingRoute: ["Calculating route…", "Расчёт маршрута…"],
  retryRouteCalculation: ["Retry calculation", "Повторить расчёт"],
  routePointActions: ["Select a track, then add it as an intermediate point or set it as the destination.", "Выберите путь, затем добавьте его как промежуточную точку или задайте конечной точкой."],
  ROUTE_CALCULATION_TIMEOUT: ["The route calculation server did not respond. Retry when the connection is restored.", "Сервер расчёта маршрута не ответил. Повторите после восстановления соединения."],
  ROUTE_CALCULATION_INVALIDATED: ["The railway changed repeatedly during calculation. Retry using the current state.", "Сеть неоднократно изменилась во время расчёта. Повторите расчёт по актуальному состоянию."],
  ROUTE_STATE_NOT_CONFIRMED: ["The server did not return a confirmed route. Check the route list before trying again.", "Сервер не передал подтверждённый маршрут. Проверьте список маршрутов перед повторной попыткой."],
  cancelRouteEdit: ["Discard proposed change", "Отклонить изменение"],
  confirmingRouteEdit: ["Applying route change…", "Применение изменения маршрута…"],
  RESERVATION_ROLLBACK_FAILED: ["The old reservation could not be fully restored. Check the route and release its remaining reservations.", "Не удалось полностью восстановить прежний резерв. Проверьте маршрут и освободите оставшиеся резервы."],
  waitingRoute: ["Receiving the confirmed route…", "Получение подтверждённого маршрута…"],
  routeWaypoints: ["Intermediate points", "Промежуточные точки"],
  addWaypoint: ["Add intermediate point", "Добавить промежуточную точку"],
  removeWaypoint: ["Remove intermediate point", "Удалить промежуточную точку"],
  moveWaypointUp: ["Move point up", "Переместить точку вверх"],
  moveWaypointDown: ["Move point down", "Переместить точку вниз"],
  clearWaypoints: ["Clear intermediate points", "Очистить промежуточные точки"],
  routeWaypointInvalid: ["This track cannot be used as an intermediate point", "Этот путь нельзя использовать как промежуточную точку"],
  signalTooltip: ["Signal preview on hover", "Показание светофора при наведении"],
  switchClick: ["Switch arrows by clicking the map", "Переводить стрелки кликом по карте"],
  availability: ["Availability", "Доступность"],
  suspended: ["Waiting for Persistent Jobs resume", "Ожидание восстановления Persistent Jobs"],
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
  protectionLegend: ["Signal block protection", "Защита сигнального блока"],
  nativeProtection: ["Native signal protection footprint", "Защитная зона сигнального блока"],
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
  resetBrowserSettings: ["Reset browser settings", "Сбросить настройки браузера"],
  confirmBrowserReset: ["Reset and reload", "Сбросить и перезагрузить"],
  browserResetScope: [
    "Restore defaults for this browser: theme and colours, interface scale, visual sizes and LOD, map layers and groups, panel sizes, camera, tracking and open views. The page will reload. Access profiles, passwords, sign-in and Host/game settings stay unchanged.",
    "Восстановить настройки этого браузера: тему и цвета, масштаб интерфейса, размеры и детализацию объектов, слои и группы карты, размеры панелей, камеру, слежение и открытые разделы. Страница перезагрузится. Профили доступа, пароли, сеанс и настройки хоста/игры сохранятся.",
  ],
  browserResetReloading: ["Restoring defaults and reloading…", "Восстановление настроек и перезагрузка…"],
  browserResetFailed: ["Could not reset browser settings. Check browser storage permissions, then try again.", "Не удалось сбросить настройки браузера. Проверьте разрешения на хранение данных и повторите попытку."],
  connecting: ["Connecting", "Подключение"],
  synchronizing: ["Waiting for railway state", "Ожидание состояния сети"],
  connectionRetrying: ["Reconnecting automatically.", "Переподключение выполняется автоматически."],
  connectionRetriesStopped: ["Automatic retries stopped after repeated failures. Check the Host and network, then choose Retry connection.", "Автоматические попытки остановлены после повторных сбоев. Проверьте хост и сеть, затем нажмите «Повторить подключение»."],
  reconnectFailed: ["Connection failed", "Не удалось подключиться"],
  retryConnection: ["Retry connection", "Повторить подключение"],
  checkingSession: ["Checking session", "Проверка сеанса"],
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
  mobileMap: ["Map", "Карта"],
  mobileNavigation: ["Mobile navigation", "Мобильная навигация"],
  mobileMore: ["More sections", "Другие разделы"],
  mobileOpenList: ["Open section", "Открыть раздел"],
  search: ["Locomotives, wagons, tracks…", "Локомотивы, вагоны, пути…"],
  activeOnly: ["Active", "Активные"],
  advisoryShort: ["Route reservations", "Резервации маршрутов"],
  advisory: [
    "Planning aligns the route. Normal reservations allow manual switches; protected reservations lock them until release.",
    "Планирование выравнивает маршрут. Обычный резерв допускает ручной перевод, защищённый блокирует стрелки до снятия резерва.",
  ],
  fit: ["Fit network", "Вся сеть"],
  playerHome: ["Show players", "Показать игроков"],
  follow: ["Follow", "Следить"],
  stopFollowing: ["Stop following", "Остановить слежение"],
  followSelection: ["Follow the selected moving object", "Следить за выбранным подвижным объектом"],
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
  weather: ["Weather", "Погода"],
  labels: ["Labels", "Подписи"],
  username: ["Web account name", "Имя веб-учётной записи"],
  password: ["Password", "Пароль"],
  signIn: ["Sign in", "Войти"],
  signInHint: [
    "Use the web account created by the host to view or control this dispatch console. Your in-game name does not grant access.",
    "Для просмотра и управления диспетчерской нужна веб-учётная запись, созданная хостом. Игровой ник не даёт прав доступа.",
  ],
  loginHelp: [
    "Hosting the game? Open dispatcher in Unity Mod Manager, choose Owner access here and paste the copied code within 2 minutes. Other users receive an account name and password from the host.",
    "Вы хост игры? Нажмите «Открыть диспетчерскую» в Unity Mod Manager, выберите здесь «Вход владельца» и вставьте скопированный код в течение 2 минут. Остальным пользователям имя и пароль выдаёт хост.",
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
  endRoute: ["Set as destination", "Установить конечной точкой"],
  preview: ["Preview", "Предпросмотр"],
  plan: ["Build route", "Собрать маршрут"],
  applyRoute: ["Align route", "Выставить маршрут"],
  cancelRoute: ["Cancel route", "Отменить маршрут"],
  clear: ["Clear", "Очистить"],
  destination: ["Destination", "Назначение"],
  job: ["Job", "Задание"],
  mass: ["Current mass", "Текущая масса"],
  weight: ["Weight", "Масса"],
  consistWeight: ["Total consist weight", "Общая масса состава"],
  massUnavailable: ["Mass unavailable", "Масса недоступна"],
  tractionCapacity: ["Traction capacity", "Тяговая способность"],
  tractionUnavailable: ["Traction data unavailable from the game", "Данные о тяге недоступны в игре"],
  loadRatingReference: ["Catalogue load-rating reference", "Справочный рейтинг нагрузки из каталога"],
  gradeDry: ["Dry rail", "Сухой путь"],
  gradeWet: ["Wet rail", "Мокрый путь"],
  generatedTraction: ["Current generated traction", "Текущая создаваемая тяга"],
  generatedTractionHint: ["Sum of locomotive traction magnitudes. Opposing directions are not subtracted.", "Сумма модулей тяги локомотивов. Противоположные направления не вычитаются."],
  tractionAssessment: ["Assessment", "Оценка"],
  tractionAssessmentBasis: ["Catalogue load limit at 2% grade", "Каталожный предел нагрузки на уклоне 2%"],
  tractionGoodReserve: ["Good reserve", "Хороший запас"],
  tractionSufficient: ["Sufficient", "Достаточно"],
  tractionInsufficient: ["Insufficient", "Недостаточно"],
  tractionUnknown: ["Unavailable", "Недоступно"],
  currentWeather: ["Current weather", "Текущая погода"],
  weatherForecast: ["Forecast", "Прогноз"],
  AUTH_STALE: ["The sign-in request was superseded by a newer session action.", "Запрос входа отменён последующим действием с сеансом."],
  weatherClearNight: ["Clear night", "Ясная ночь"],
  weatherOvercast: ["Overcast", "Пасмурно"],
  weatherPartlyCloudyDay: ["Partly cloudy", "Переменная облачность"],
  weatherPartlyCloudyNight: ["Partly cloudy night", "Переменная облачность ночью"],
  weatherLightRainDay: ["Light rain", "Небольшой дождь"],
  weatherLightRainNight: ["Light rain at night", "Небольшой дождь ночью"],
  weatherLightFogDay: ["Light fog", "Лёгкий туман"],
  weatherLightFogNight: ["Light fog at night", "Лёгкий туман ночью"],
  weatherHeavyFog: ["Heavy fog", "Густой туман"],
  weatherForecastNow: ["Current period", "Текущий период"],
  weatherForecastIn: ["In {time}", "Через {time}"],
  weatherForecastDuration: ["Period: {time}", "Период: {time}"],
  weatherForecastUnavailable: ["The game runtime did not provide a forecast", "Игра не предоставила прогноз"],
  weatherUnavailable: ["Weather data unavailable", "Данные о погоде недоступны"],
  weatherClear: ["Clear", "Ясно"],
  weatherCloudy: ["Cloudy", "Облачно"],
  weatherRain: ["Rain", "Дождь"],
  weatherHeavyRain: ["Heavy rain", "Сильный дождь"],
  weatherThunder: ["Thunderstorm", "Гроза"],
  weatherFog: ["Fog", "Туман"],
  weatherUnknown: ["Unknown", "Неизвестно"],
  wetness: ["Surface wetness", "Влажность поверхности"],
  weatherRainAmount: ["Rain intensity", "Интенсивность дождя"],
  weatherCloudinessAmount: ["Cloudiness", "Облачность"],
  weatherThunderAmount: ["Thunder intensity", "Интенсивность грозы"],
  weatherFogAmount: ["Fog density", "Плотность тумана"],
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
  independentBrake: ["Independent brake", "Локомотивный тормоз"],
  reverser: ["Reverser", "Реверс"],
  brakePipe: ["Brake pipe", "Тормозная магистраль"],
  pressureBar: ["bar", "бар"],
  uncouplingPosition: ["Coupling to release", "Место расцепки"],
  couplerFront: ["Front · {index}", "Спереди · {index}"],
  couplerRear: ["Rear · {index}", "Сзади · {index}"],
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
  accounts: ["Access profiles", "Профили доступа"],
  profilesIntro: ["Manage who can open this dispatcher and which commands they can use.", "Настройте, кто может открывать диспетчерскую и какие команды ему доступны."],
  existingProfiles: ["Existing profiles", "Существующие профили"],
  createProfile: ["Create profile", "Создать профиль"],
  editProfile: ["Edit selected profile", "Изменить выбранный профиль"],
  selectProfile: ["Select a profile", "Выберите профиль"],
  chooseProfileHint: ["Select an existing profile to change its access or password.", "Выберите существующий профиль, чтобы изменить права или пароль."],
  profileIdentity: ["Profile name", "Имя профиля"],
  profileName: ["Sign-in name", "Имя для входа"],
  profileNameHint: ["1–48 letters, digits, _ or -. Case-sensitive; spaces are not allowed.", "1–48 букв, цифр, _ или -. Регистр учитывается, пробелы недопустимы."],
  profileNameInvalid: ["Enter 1–48 letters, digits, _ or -, without spaces. The name local-owner is reserved.", "Введите 1–48 букв, цифр, _ или - без пробелов. Имя local-owner зарезервировано."],
  profilePermissions: ["Permissions", "Права доступа"],
  profileViewerPermissions: ["View the railway, trains and jobs. Game commands are unavailable.", "Просмотр сети, поездов и заданий. Игровые команды недоступны."],
  profileDispatcherPermissions: ["Manage routes, jobs, switches and signals. Profile administration and locomotive controls remain with the Host owner.", "Управление маршрутами, заданиями, стрелками и сигналами. Профили и управление локомотивами доступны только владельцу хоста."],
  profileSecurity: ["Password", "Пароль"],
  changeProfilePassword: ["Change password", "Изменить пароль"],
  newProfilePassword: ["New password", "Новый пароль"],
  profilePasswordHint: ["10–128 characters. Share it with this profile's user.", "10–128 символов. Передайте пароль пользователю этого профиля."],
  profilePasswordInvalid: ["Enter a password of 10–128 characters, not only spaces.", "Введите пароль из 10–128 символов, состоящий не только из пробелов."],
  profilePasswordUnchanged: ["The current password will stay unchanged.", "Текущий пароль будет сохранён."],
  profileSessionHint: ["Saving changes ends this profile's active sessions.", "Сохранение изменений завершает активные сеансы этого профиля."],
  saveProfileChanges: ["Save changes", "Сохранить изменения"],
  profileSaving: ["Saving…", "Сохранение…"],
  profileCreated: ["Profile created. Its user can now sign in.", "Профиль создан. Пользователь может войти."],
  noProfiles: ["No access profiles yet", "Профилей доступа пока нет"],
  noProfilesHint: ["Create a viewer or dispatcher profile to give another user access.", "Создайте профиль наблюдателя или диспетчера, чтобы предоставить доступ другому пользователю."],
  createFirstProfile: ["Create the first profile", "Создать первый профиль"],
  profileDeleteHeading: ["Delete this profile", "Удаление профиля"],
  profileDeleteHint: ["Removes access and ends its sessions. This cannot be undone.", "Доступ будет удалён, сеансы завершатся. Действие нельзя отменить."],
  confirmProfileDelete: ["Confirm deletion", "Подтвердить удаление"],
  ACCOUNT_EXISTS: ["A profile with this exact name already exists. Choose another name or edit that profile.", "Профиль с таким именем уже существует. Выберите другое имя или измените существующий профиль."],
  ACCOUNT_NOT_FOUND: ["This profile no longer exists. Refresh the list and select a profile again.", "Этот профиль больше не существует. Обновите список и выберите профиль заново."],
  ACCOUNT_LIMIT: ["The limit of 100 profiles has been reached. Delete an unused profile before creating another.", "Достигнут предел в 100 профилей. Удалите неиспользуемый профиль перед созданием нового."],
  role: ["Role", "Роль"],
  viewer: ["Viewer", "Наблюдатель"],
  dispatcher: ["Dispatcher", "Диспетчер"],
  admin: ["Administrator", "Администратор"],
  deleteAccount: ["Delete profile", "Удалить профиль"],
  confirmDeleteAccount: ["Delete this profile and end its active sessions?", "Удалить этот профиль и завершить его активные сеансы?"],
  accountDeleted: ["Profile deleted. Its sessions were revoked.", "Профиль удалён. Его сеансы завершены."],
  loadingAccounts: ["Loading access profiles…", "Загрузка профилей доступа…"],
  reloadAccounts: ["Refresh profiles", "Обновить профили"],
  INVALID_ACCOUNT: ["Check the profile name, role and password (10–128 characters for a new password).", "Проверьте имя, роль и пароль профиля (10–128 символов для нового пароля)."],
  hostConnection: ["Web Host connection", "Подключение к Web Host"],
  certificateSetup: ["HTTPS certificate setup", "Настройка сертификата HTTPS"],
  loadingConnection: ["Loading connection details…", "Загрузка сведений о подключении…"],
  remoteAccessEnabled: ["Remote access is enabled. Use an address reachable from the client device.", "Удалённый доступ включён. Используйте адрес, доступный с клиентского устройства."],
  remoteAccessDisabled: ["Remote access is disabled. Only this computer can connect.", "Удалённый доступ выключен. Подключение доступно только с этого компьютера."],
  addressLocal: ["This computer", "Этот компьютер"],
  addressLan: ["Local network", "Локальная сеть"],
  addressVpn: ["Radmin VPN", "Radmin VPN"],
  addressPublic: ["Public address", "Публичный адрес"],
  certificateTrustHelp: [
    "This Host uses a local certificate authority (CA), which browsers do not trust automatically. Download its public certificate here or copy Host/data/dispatcher-ca.cer. Compare the SHA-256 fingerprint with the Host, then install only this Host CA as trusted on each client device. Keep browser security checks enabled. Detailed steps are in the mod README.",
    "Этот Host использует локальный центр сертификации (CA), которому браузеры не доверяют автоматически. Скачайте его открытый сертификат здесь или скопируйте Host/data/dispatcher-ca.cer. Сверьте отпечаток SHA-256 с хостом, затем добавьте в доверенные только этот CA на каждом клиентском устройстве. Не отключайте проверки безопасности браузера. Подробные шаги — в README мода.",
  ],
  certificateFingerprint: ["CA fingerprint (SHA-256)", "Отпечаток CA (SHA-256)"],
  certificateExpires: ["CA valid until", "CA действует до"],
  downloadCertificate: ["Download public CA certificate", "Скачать открытый сертификат CA"],
  retryConnectionInfo: ["Retry connection details", "Повторить загрузку сведений"],
  accountSaved: [
    "Profile saved. Previous sessions were revoked.",
    "Профиль сохранён. Его прежние сеансы завершены.",
  ],
  accountFailed: [
    "Could not complete the profile change. Try again.",
    "Не удалось изменить профиль. Повторите попытку.",
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
    "Remote LAN Access and the port are configured in Unity Mod Manager. Remote access enables HTTPS automatically.",
    "Параметр «Разрешить удалённый доступ по LAN» и порт настраиваются в Unity Mod Manager. Удалённый доступ автоматически включает HTTPS.",
  ],
  findTrack: ["Find track", "Найти путь"],
  rescan: ["Refresh topology", "Обновить сеть"],
  exportLog: ["Export event log", "Сохранить журнал"],
  AUTH_REQUIRED: ["Sign in first", "Сначала войдите"],
  AUTH_EXPIRED: ["Your session has ended. Sign in again.", "Сеанс завершён. Войдите снова."],
  HOST_STATE_TIMEOUT: ["The connection opened, but railway state did not arrive in time. Check the Web Host and game connection.", "Соединение открыто, но состояние сети не получено вовремя. Проверьте Web Host и соединение с игрой."],
  HOST_BAD_RESPONSE: ["Web Host returned an unreadable response. Check that the Host and browser files are the same version.", "Web Host вернул некорректный ответ. Проверьте совпадение версий Host и файлов веб-интерфейса."],
  SESSION_ACTIVE: [
    "This account is already signed in elsewhere. Sign out there or wait for its connection timeout.",
    "Эта учётная запись уже используется. Выйдите из другого сеанса или дождитесь тайм-аута соединения.",
  ],
  HOST_TIMEOUT: [
    "The Web Host did not respond in time. Check the address, port and firewall.",
    "Web Host не ответил вовремя. Проверьте адрес, порт и firewall.",
  ],
  HOST_UNAVAILABLE: [
    "Unable to connect to Web Host. Check the address, port, HTTPS and network.",
    "Не удалось подключиться к Web Host. Проверьте адрес, порт, HTTPS и сеть.",
  ],
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
  INSECURE_TRANSPORT: [
    "Remote login requires HTTPS. Enable Remote LAN Access in Unity Mod Manager and open the HTTPS address shown by the Host.",
    "Для удалённого входа нужен HTTPS. Включите «Разрешить удалённый доступ по LAN» в Unity Mod Manager и откройте HTTPS-адрес, указанный хостом.",
  ],
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
  routePlanned: ["Route built", "Маршрут собран"],
  routeCancelled: ["Plan cancelled", "План отменён"],
  setSwitch: ["Switch command", "Команда стрелке"],
  loco: ["Locomotive command", "Команда локомотиву"],
  setSignalMode: ["Signal mode changed", "Изменён режим сигнала"],
  setSignalAspect: ["Signal override changed", "Изменено перекрытие сигнала"],
  setShunting: ["Shunting setting changed", "Изменён маневровый режим"],
  cancelSignalReservation: ["Reservation cleared", "Резерв снят"],
  planRoute: ["Route built", "Маршрут собран"],
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
    "The route's signal block needs to be rechecked",
    "Сигнальный блок маршрута требует повторной проверки",
  ],
  SIGNAL_PROTECTION_CHANGED: [
    "The reserved signal block's protection footprint changed",
    "Изменился защищаемый участок зарезервированного сигнального блока",
  ],
  SIGNAL_ROUTE_MISMATCH: [
    "The signal block does not follow the selected route. Recalculate the route.",
    "Сигнальный блок не соответствует выбранному пути. Пересчитайте маршрут.",
  ],
  AUTO_ROUTE_RECALCULATING: [
    "The proposed route change is ready for review",
    "Предпросмотр изменения маршрута готов к проверке",
  ],
  AUTO_ROUTE_RECALCULATION_STARTED: ["Calculating a replacement path for the route", "Рассчитывается новый путь маршрута"],
  AUTO_ROUTE_RECALCULATION_AVAILABLE: ["Route needs recalculation", "Маршрут требует перерасчёта"],
  AUTO_RECALCULATED: [
    "The route was updated after recalculation",
    "Маршрут обновлён после перерасчёта",
  ],
  NO_ALTERNATIVE_ROUTE: [
    "The route cannot be rebuilt: no available path to the destination",
    "Маршрут невозможно перестроить: доступного пути до назначения нет",
  ],
  ROUTE_RECALCULATION_FAILED: [
    "Route recalculation failed; inspect the route state",
    "Перерасчёт маршрута не выполнен; проверьте состояние маршрута",
  ],
  ROUTE_RECALCULATING: [
    "This route is being recalculated; the current reservation is still active",
    "Маршрут пересчитывается; текущая резервация пока сохраняется",
  ],
  confirmRouteEdit: ["Confirm route change", "Подтвердить изменение маршрута"],
  routeEditPreview: ["Proposed path", "Предлагаемый путь"],
  ROUTE_RECALCULATION_STALE: [
    "The network changed; calculate a fresh route preview",
    "Состояние сети изменилось; требуется новый предпросмотр маршрута",
  ],
  ROUTE_RECALCULATION_LIMIT: [
    "Route recalculation limit reached; inspect the current route",
    "Достигнут предел перерасчётов; проверьте текущий маршрут",
  ],
  routeEdited: ["Route updated", "Маршрут обновлён"],
  recalculating: ["Recalculating", "Перерасчёт"],
  recalculateRoute: ["Recalculate route", "Пересчитать маршрут"],
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
  dispatcherAccessHelp: [
    "Dispatcher: inspect the railway and control infrastructure and routes. Rolling stock control is reserved for the host owner.",
    "Диспетчер: просмотр сети, управление инфраструктурой и маршрутами. Управление подвижным составом доступно только владельцу хоста.",
  ],
  viewerAccessHelp: [
    "Viewer: open any object's details and use visual map settings. Game commands are unavailable.",
    "Наблюдатель: сведения об объектах и визуальные настройки карты. Игровые команды недоступны.",
  ],
  localOwner: ["Host owner", "Владелец хоста"],
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

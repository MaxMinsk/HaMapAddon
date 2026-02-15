# Development Plan: Home Assistant People Map Plus

## 1. Цель и ожидаемый результат

Собрать решение из двух частей:

1. `custom:people-map-plus` (Lovelace card) для визуализации людей, треков, остановок, фото и статистики на карте.
2. Custom Integration для Home Assistant, которая индексирует JPEG-фото с EXIF GPS, хранит метаданные и отдает API для фронтенда.

Итог: рабочий MVP, который можно установить в HA, настроить через YAML, и использовать для отображения до 5 пользователей с треками и фото-слоем.

## 2. Scope (MVP vs Next)

### MVP

1. До 5 пользователей (`person.*` + опционально `device_tracker.*`).
2. Отрисовка текущих позиций и трека за последние `N` часов.
3. Автоматическое определение остановок по порогу времени.
4. Фото-слой из EXIF GPS (маркеры + кластеры + предпросмотр).
5. Базовая статистика: дистанция, время в движении, время в остановке.
6. Настройки через YAML, включая цвета/аватары/иконки и включение слоев.

### Post-MVP

1. Произвольный диапазон дат (не только последние N часов).
2. Расширенная аналитика (скорость, heatmap, сравнение дней).
3. UI-редактор настроек через визуальный конфиг-редактор.
4. Фоновая переиндексация по расписанию/событию.

## 3. Архитектурные решения

## 3.1 Frontend (Lovelace Card)

1. Технологии: TypeScript + Lit + Leaflet.
2. Основные слои карты:
   - People layer (актуальные позиции).
   - Track layer (история движения).
   - Stops layer (точки остановок).
   - Photo layer (GPS-фото + кластеризация).
3. Компоненты:
   - `people-map-plus-card.ts` (основной lifecycle + конфиг).
   - `map-controller.ts` (инициализация/обновление Leaflet).
   - `layers/*` (независимые модули слоев).
   - `services/ha-api.ts` (вызовы API HA + custom integration).
   - `utils/geo.ts` (distance, speed, dedupe, stop detection).

## 3.2 Backend (Custom Integration)

1. Domain: `people_map_plus`.
2. Компоненты:
   - `manifest.json` (зависимости/версия/домен).
   - `__init__.py` (setup/unload, registry, scheduler).
   - `api.py` (REST/websocket endpoints).
   - `photo_indexer.py` (скан `/media`, EXIF парсер, индексатор).
   - `storage.py` (SQLite schema + запросы).
   - `services.yaml` (ручной `reindex`, `cleanup`).
3. API задачи:
   - Отдать фото по bbox + time range + limit.
   - Отдать фото рядом с треком/точкой.
   - Вернуть агрегированные метаданные для карты.

## 4. Структура репозитория

```text
/custom_components/people_map_plus/
  manifest.json
  __init__.py
  api.py
  photo_indexer.py
  storage.py
  services.yaml
  translations/en.json

/frontend/people-map-plus/
  src/
    people-map-plus-card.ts
    map-controller.ts
    config.ts
    layers/
      people-layer.ts
      track-layer.ts
      stops-layer.ts
      photos-layer.ts
    services/
      ha-api.ts
    utils/
      geo.ts
      time.ts
      track.ts
  package.json
  tsconfig.json

/docs/
  architecture.md
  api.md
  config.md

README.md
development_plan.md
```

## 5. Модель данных и алгоритмы

## 5.1 Track points (frontend)

Источник: History API HA по `device_tracker.*`/`person.*`.

Поля точки:

1. `entity_id`
2. `lat`, `lon`
3. `timestamp`
4. `accuracy` (если доступно)
5. `source` (`person` или `tracker`)

Обработка:

1. Удаление дублей координат по epsilon.
2. Фильтр точек с явно некорректной скоростью.
3. Опциональное упрощение полилинии (RDP).

## 5.2 Stops detection

Базовое правило MVP:

1. Кластер последовательных точек в радиусе `R` метров.
2. Если суммарная длительность кластера >= `X` минут, это остановка.

Рекомендации по дефолтам:

1. `R = 40m`
2. `X = 10min`

## 5.3 Photo index schema (SQLite)

Таблица `photos`:

1. `id` (TEXT/UUID)
2. `path` (TEXT, unique)
3. `taken_at` (INTEGER epoch)
4. `lat` REAL
5. `lon` REAL
6. `has_exif_gps` INTEGER
7. `thumb_path` TEXT (optional)
8. `created_at` INTEGER
9. `updated_at` INTEGER

Индексы:

1. `(lat, lon)`
2. `(taken_at)`
3. `(has_exif_gps, taken_at)`

## 6. API контракт (черновой)

## 6.1 `GET /api/people_map_plus/photos`

Параметры:

1. `bbox=minLon,minLat,maxLon,maxLat` (optional)
2. `from` / `to` (epoch or ISO, optional)
3. `limit` (default 500)

Ответ:

1. `items[]` с `path`, `lat`, `lon`, `taken_at`, `thumbnail`
2. `meta` (`count`, `bbox`, `truncated`)

## 6.2 `POST /api/people_map_plus/reindex`

Задача: вручную запустить индексацию `/media`.

Ответ:

1. `job_id`
2. `status`

## 6.3 `GET /api/people_map_plus/health`

Проверка доступности интеграции и статуса индекса.

## 7. Конфигурация карты (MVP)

Поддерживаемые параметры:

1. `persons[]`
2. `map.hours`
3. `map.show_zones`
4. `layers.track`
5. `layers.stops`
6. `layers.photos`
7. `stops.radius_m`
8. `stops.min_minutes`

Валидация:

1. Не более 5 `persons`.
2. Проверка entity существования.
3. Дефолты для отсутствующих полей.

## 8. План реализации по этапам

## Этап 0: Bootstrap проекта (0.5-1 день)

1. Создать структуру директорий frontend/backend/docs.
2. Добавить базовые `README.md`, `manifest.json`, `package.json`.
3. Настроить сборку frontend-части (lint + build).

Definition of Done:

1. Проект собирается локально.
2. Card регистрируется в HA как custom card (пустой рендер).

## Этап 1: Базовая карта и люди (1-2 дня)

1. Подключить Leaflet.
2. Реализовать конфиг `persons`.
3. Показать текущие маркеры людей с аватаром/иконкой.
4. Поддержать обновление состояния по событиям HA.

DoD:

1. До 5 пользователей отображаются стабильно.
2. Нет падений при недоступном tracker.

## Этап 2: Треки и фильтр N часов (1-2 дня)

1. Получение истории координат из HA.
2. Построение полилиний + цвета по пользователям.
3. Дедуп/упрощение точек.
4. UI-контрол `hours` (из конфига и, при желании, quick-switch).

DoD:

1. Трек корректно отображается за последние N часов.
2. Производительность приемлема на 5 пользователях.

## Этап 3: Остановки и статистика (1-2 дня)

1. Реализовать алгоритм остановок (`R`, `X`).
2. Визуализация остановок отдельным слоем.
3. Подсчет distance/moving/stopped.
4. Плашка статистики по каждому пользователю.

DoD:

1. Остановки совпадают с ожидаемым поведением на тестовых данных.
2. Статистика пересчитывается при изменении диапазона времени.

## Этап 4: Backend интеграция для фото (2-3 дня)

1. Создать custom integration `people_map_plus`.
2. Реализовать скан `/media` и парсинг EXIF GPS JPEG.
3. Сохранение в SQLite + индексы.
4. API для чтения фото по bbox/time.
5. Сервис ручной реиндексации.

DoD:

1. Фото с GPS доступны через API.
2. Повторная индексация не дублирует записи.

## Этап 5: Фото-слой на карте (1-2 дня)

1. Интеграция card с backend API.
2. Кластеризация маркеров фото.
3. Popup с превью и ссылкой на оригинал.
4. Подгрузка по текущему bbox карты.

DoD:

1. Карта не тормозит при сотнях фото.
2. Фото корректно фильтруются по времени и видимой области.

## Этап 6: Hardening и релиз (1-2 дня)

1. Обработка edge cases и ошибок API.
2. Логи и отладочные флаги.
3. Документация установки и конфигурации.
4. Smoke-тест в реальном HA инстансе.
5. Версионирование и release notes.

DoD:

1. Подготовлен релиз `v0.1.0` (MVP).
2. Есть инструкция по обновлению и известным ограничениям.

## 9. Тестирование

## 9.1 Unit tests

1. Гео-утилиты (distance, dedupe, stop detection).
2. Нормализация track points.
3. Парсинг EXIF + SQLite query helpers.

## 9.2 Integration tests

1. API backend: bbox/time filters и limit.
2. Реиндексация и идемпотентность.

## 9.3 Manual QA checklist

1. 1-5 пользователей на карте.
2. Потеря сети/ошибка API не ломает UI.
3. Переключение слоев работает без перерендера всей карты.
4. Фото без GPS игнорируются корректно.
5. Большой трек (10k+ точек) не блокирует UI (или деградирует контролируемо).

## 10. Нефункциональные требования

1. Производительность:
   - Перерисовка карты < 200ms на типовом сценарии.
   - Ленивая подгрузка фото по bbox.
2. Надежность:
   - Устойчивость к отсутствующим координатам/битым EXIF.
3. Безопасность:
   - API в рамках auth Home Assistant.
   - Ограничение доступа к файловым путям, только ожидаемые media routes.

## 11. Риски и меры

1. Риск: нестабильные данные от трекеров.
   Мера: фильтры выбросов, fallback на последнюю валидную точку.
2. Риск: медленная индексация больших фотоархивов.
   Мера: инкрементальный индекс + лимит по батчам + фоновая задача.
3. Риск: фронтенд лаги на больших треках.
   Мера: simplification + ограничение точек + web worker (post-MVP).
4. Риск: различия форматов EXIF.
   Мера: fallback-библиотеки + логирование причин пропуска файла.

## 12. План поставки

1. `v0.1.0` (MVP):
   - Люди, треки, остановки, базовая статистика, фото-слой.
2. `v0.2.0`:
   - Произвольные диапазоны времени + улучшенная аналитика.
3. `v0.3.0`:
   - Оптимизации производительности + UI конфиг-редактор.

## 13. Ближайшие практические шаги

1. Утвердить стек frontend (`Lit + Leaflet`) и backend (SQLite + EXIF library).
2. Создать каркас проекта (этап 0) и первый рабочий рендер карты.
3. После этапа 1-2 собрать демонстрацию на реальных `person.*`.
4. Затем подключить backend фото-слой и выйти на MVP.

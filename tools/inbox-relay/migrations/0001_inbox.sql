-- Source-agnostic envelope: one row per inbound message awaiting Roamint (see docs/features/0024).
-- AUTOINCREMENT so ids are never reused after an ack deletes rows; Roamint dedups on (source, external_id).
CREATE TABLE inbox (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    source      TEXT NOT NULL,
    external_id TEXT NOT NULL,
    received_at TEXT NOT NULL,
    payload     TEXT NOT NULL,
    UNIQUE (source, external_id)
);

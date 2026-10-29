# Database snapshot

`osynix-ats.db` is a standalone SQLite snapshot of the recovered local database, created on 2026-10-29. It contains application data and user accounts. SQLite integrity_check passed when the snapshot was created.

The desktop launcher continues to use the working database at `%LOCALAPPDATA%\OsynixATS\Data\osynix-ats-recovered-20261029.db`. This snapshot is not updated automatically when the app changes data.

Keep SQLite `-wal` and `-shm` files out of Git. Refresh this file using the SQLite backup API so committed snapshots include all committed transactions.

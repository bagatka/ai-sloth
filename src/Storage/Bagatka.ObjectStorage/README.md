# Bagatka.ObjectStorage

Objects by key, for what outlives a nook: checkpoints of its files and its agent's sessions, and
people's harness state. General-purpose: it knows nothing about AiSloth.

- **Contract:** `IObjectStorage`: put an object whole, open one to read with its length known,
  delete everything under a prefix. Keys are lowercase segments separated by `/` (`ObjectKeys`).
  Who writes which keys, and in which order with their rows, is `PATTERNS.md`, entry 13.
- **Backends:** `FileSystemObjectStorage`, one file per object in a directory of this computer, for
  a host on one server and for development. An S3-compatible backend comes with hosting.
- **Not encrypted here.** Objects hold people's code and conversations: the directory, or later the
  bucket, is protected like the database.

## Not built yet

- An S3-compatible backend, and listing keys.

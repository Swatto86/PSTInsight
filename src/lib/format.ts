// Presentation helpers: dates and byte sizes.

const DATE_FMT = new Intl.DateTimeFormat(undefined, {
  year: "numeric",
  month: "short",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
});

const TIME_FMT = new Intl.DateTimeFormat(undefined, {
  hour: "2-digit",
  minute: "2-digit",
});

const DAY_FMT = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "2-digit",
});

/** Full date/time for the reading pane. `unixSeconds` is seconds since epoch. */
export function formatDate(unixSeconds: number | null): string {
  if (unixSeconds == null) return "";
  return DATE_FMT.format(new Date(unixSeconds * 1000));
}

/** Compact date for the list: time today, "Mon DD" this year, else year. */
export function formatListDate(unixSeconds: number | null): string {
  if (unixSeconds == null) return "";
  const d = new Date(unixSeconds * 1000);
  const now = new Date();
  if (d.toDateString() === now.toDateString()) return TIME_FMT.format(d);
  if (d.getFullYear() === now.getFullYear()) return DAY_FMT.format(d);
  return String(d.getFullYear());
}

const UNITS = ["B", "KB", "MB", "GB", "TB"];

export function formatSize(bytes: number): string {
  if (bytes <= 0) return "0 B";
  const i = Math.min(UNITS.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  const value = bytes / Math.pow(1024, i);
  return `${value >= 100 || i === 0 ? Math.round(value) : value.toFixed(1)} ${UNITS[i]}`;
}

/** A short display label for a sender (name, falling back to email). */
export function senderLabel(name: string, email: string): string {
  return name?.trim() || email?.trim() || "(unknown sender)";
}

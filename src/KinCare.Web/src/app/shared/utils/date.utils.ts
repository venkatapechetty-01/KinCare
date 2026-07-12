import {
  formatDistanceToNow,
  format,
  isToday,
  isTomorrow,
  isYesterday,
  parseISO,
  differenceInMinutes,
  addMinutes,
} from 'date-fns';

export function lastSeenLabel(lastLocationAt: string | null | undefined): string {
  if (!lastLocationAt) return 'No GPS';
  const date = parseISO(lastLocationAt);
  const diffMin = differenceInMinutes(new Date(), date);
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin} min ago`;
  return format(date, 'h:mm a');
}

export function formatPickupTime(isoString: string): string {
  const date = parseISO(isoString);
  if (isToday(date)) return `Today · ${format(date, 'h:mm a')}`;
  if (isTomorrow(date)) return `Tomorrow · ${format(date, 'h:mm a')}`;
  if (isYesterday(date)) return `Yesterday · ${format(date, 'h:mm a')}`;
  return format(date, 'EEE, MMM d · h:mm a');
}

export function formatShortTime(isoString: string): string {
  return format(parseISO(isoString), 'h:mm a');
}

export function formatDateForInput(date: Date): string {
  return format(date, 'yyyy-MM-dd');
}

export function formatTimeForInput(date: Date): string {
  return format(date, 'HH:mm');
}

export function offsetNow(offsetMinutes: number): { date: string; time: string } {
  const d = addMinutes(new Date(), offsetMinutes);
  return {
    date: formatDateForInput(d),
    time: formatTimeForInput(d),
  };
}

export function rideTimeAgo(isoString: string): string {
  return formatDistanceToNow(parseISO(isoString), { addSuffix: true });
}

export type HoldStatus = 'HELD' | 'CONFIRMED' | 'EXPIRED' | 'CANCELLED';

export interface SchoolClass {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}

export interface Hold {
  holdId: string;
  classId: string;
  className: string;
  status: HoldStatus;
  expiresAt: string;
  studentId?: string;
  createdAt?: string;
}

export interface CreateClassInput {
  name: string;
  totalSeats: number;
}

export interface HealthInfo {
  status: string;
  holdDurationSeconds: number;
  serverTimeUtc: string;
}

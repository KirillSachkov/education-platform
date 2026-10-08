export interface LeaderboardUserDto {
  rank: number;
  userId: string;
  displayName: string | null;
  username: string | null;
  totalXp: number;
  currentLevel: number;
  isCurrentUser: boolean;
  avatarId: string | null;
  // #572 — глобальные счётчики активности участника (по всей платформе).
  materialsCompleted: number;
  quizzesCompleted: number;
  issuesCompleted: number;
}

export interface GetLeaderboardResponse {
  items: LeaderboardUserDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  currentUser: LeaderboardUserDto | null;
}

export interface GetLeaderboardParams {
  page?: number;
  pageSize?: number;
  authorId?: string;
  courseId?: string;
}

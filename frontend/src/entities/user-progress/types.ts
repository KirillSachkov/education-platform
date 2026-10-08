export interface UserXpProgressDto {
  totalXp: number;
  currentLevel: number;
  nextLevel: number | null;
  xpToNextLevel: number | null;
}

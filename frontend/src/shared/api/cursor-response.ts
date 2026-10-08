export interface CursorResponse<T> {
  items: T[];
  totalCount: number;
  nextCursor?: string;
}

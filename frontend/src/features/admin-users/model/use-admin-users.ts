import { usersQueryOptions, type GetUsersParams } from "@/entities/user";
import { useQuery } from "@tanstack/react-query";

export function useAdminUsers(params: GetUsersParams) {
  const { data, isLoading, error } = useQuery(
    usersQueryOptions.getUsersOptions(params),
  );

  return {
    data,
    isLoading,
    error,
  };
}

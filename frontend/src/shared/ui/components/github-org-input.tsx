"use client";

import { apiClient, type Envelope } from "@/shared/api";
import { Input } from "@/shared/ui/kit/input";
import { queryOptions, useQuery } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, Loader2 } from "lucide-react";
import Image from "next/image";
import { useEffect, useState } from "react";

export interface GitHubOrgInfo {
  login: string;
  name: string;
  avatarUrl: string;
}

const githubOrgApi = {
  validate: async (slug: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<GitHubOrgInfo>>(
      `/courses/github-orgs/${encodeURIComponent(slug)}/validate`,
      { signal },
    );
    return res.data;
  },
};

const GITHUB_ORG_QUERY_KEY = "github-org";

export const githubOrgQueryOptions = (slug: string) =>
  queryOptions({
    queryKey: [GITHUB_ORG_QUERY_KEY, slug],
    queryFn: ({ signal }) => githubOrgApi.validate(slug, { signal }),
    select: (data) => data.result!,
    enabled: !!slug && slug.length >= 2,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });

interface GitHubOrgInputProps {
  id?: string;
  value: string;
  onChange: (value: string) => void;
  onBlur?: () => void;
  placeholder?: string;
}

/**
 * Текстовый инпут с live-валидацией существования GitHub-организации
 * (debounced GET `/courses/github-orgs/{slug}/validate`). Используется в
 * формах редактирования плана (`features/author-plans/edit-plan-form`)
 * для поля `plan.github_org_slug` — backend по матчу org выдаёт PlanGrant.
 */
export function GitHubOrgInput({
  id,
  value,
  onChange,
  onBlur,
  placeholder = "my-github-org",
}: GitHubOrgInputProps) {
  const [debouncedSlug, setDebouncedSlug] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSlug(value);
    }, 500);
    return () => clearTimeout(timer);
  }, [value]);

  const { data: orgInfo, isLoading, isError } = useQuery(githubOrgQueryOptions(debouncedSlug));

  const showStatus = debouncedSlug && debouncedSlug.length >= 2;

  return (
    <div className="space-y-1.5">
      <Input
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
        placeholder={placeholder}
      />

      {showStatus && (
        <div className="flex items-center gap-2 text-xs">
          {isLoading && (
            <>
              <Loader2 className="size-3.5 animate-spin text-muted-foreground" />
              <span className="text-muted-foreground">Проверка...</span>
            </>
          )}

          {!isLoading && orgInfo && (
            <>
              <Image
                src={orgInfo.avatarUrl}
                alt={orgInfo.login}
                width={20}
                height={20}
                className="size-5 rounded-sm"
              />
              <CheckCircle2 className="size-3.5 text-green-600" />
              <span className="text-green-600 font-medium">
                {orgInfo.name}
              </span>
            </>
          )}

          {!isLoading && isError && (
            <>
              <AlertTriangle className="size-3.5 text-yellow-600" />
              <span className="text-yellow-600">
                Организация не найдена на GitHub
              </span>
            </>
          )}
        </div>
      )}
    </div>
  );
}

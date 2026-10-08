export type StudentProfile = {
  gitHubUrl: string | null;
};

export type AuthorProfile = {
  specialization: string | null;
  aboutAsAuthor: string | null;
};

export type ReviewerProfile = {
  reviewCapacity: number | null;
  expertise: string | null;
};

export type RoleProfiles = {
  student: StudentProfile | null;
  author: AuthorProfile | null;
  reviewer: ReviewerProfile | null;
};

export type MyProfile = {
  id: string;
  name: string;
  displayName: string | null;
  username: string;
  email: string;
  roles: string[];
  bio: string | null;
  profiles: RoleProfiles | null;
  hasPassword: boolean;
  hasGitHubLinked: boolean;
  hasTelegramLinked: boolean;
  avatarId: string | null;
  githubOrgs: string[];
};

export type UpdateMyBaseProfileRequest = {
  bio: string | null;
};

export type UpdateMyAuthorProfileRequest = {
  specialization: string | null;
  aboutAsAuthor: string | null;
};

export type UpdateMyReviewerProfileRequest = {
  reviewCapacity: number | null;
  expertise: string | null;
};

export type UpdateMyAccountInfoRequest = {
  username?: string;
  displayName?: string;
};

export type CompleteProfileRequest = {
  displayName: string;
};

export type PublicProfile = {
  id: string;
  displayName: string | null;
  username: string | null;
  bio: string | null;
  avatarId: string | null;
  specialization: string | null;
  aboutAsAuthor: string | null;
  gitHubUrl: string | null;
};

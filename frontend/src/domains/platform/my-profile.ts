import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

/* The signed-in user's own settings, profile picture and password (docs/designs/63-user-menu.md). The password goes
 * through ABP's account endpoint; the rest through Platform's my-profile endpoints. */

export type MyProfile = components["schemas"]["OpenTms.Platform.Accounts.MyProfileDto"];
export type UpdateMyProfile = components["schemas"]["OpenTms.Platform.Accounts.UpdateMyProfileDto"];

/** Every query of the user's own profile starts with this key; invalidate it after any change to the current user. */
export const myProfileQueryKey = ["platform", "my-profile"] as const;
const myPictureQueryKey = [...myProfileQueryKey, "picture"] as const;

export function useMyProfile(enabled = true) {
  return useQuery({
    queryKey: myProfileQueryKey,
    enabled,
    queryFn: async (): Promise<MyProfile> => {
      const { data, error } = await apiClient.GET("/api/platform/my-profile");
      if (error) throw error;
      return data!;
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function useUpdateMyProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (dto: UpdateMyProfile): Promise<MyProfile> => {
      const { data, error } = await apiClient.PUT("/api/platform/my-profile", { body: dto });
      if (error) throw error;
      return data!;
    },
    onSuccess: (profile) => queryClient.setQueryData(myProfileQueryKey, profile),
  });
}

/** The user's own picture as an object URL (null when there is none); revoked when it changes or unmounts. */
export function useMyProfilePictureUrl(hasPicture: boolean): string | null {
  const query = useQuery({
    queryKey: myPictureQueryKey,
    enabled: hasPicture,
    queryFn: async (): Promise<Blob | null> => {
      const { data, error, response } = await apiClient.GET("/api/platform/my-profile/picture", { parseAs: "blob" });
      if (response.status === 404) return null;
      if (error) throw error;
      return (data as Blob | undefined) ?? null;
    },
    staleTime: 5 * 60 * 1000,
  });

  const blob = hasPicture ? query.data : null;
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!blob) {
      setUrl(null);
      return;
    }
    const objectUrl = URL.createObjectURL(blob);
    setUrl(objectUrl);
    return () => URL.revokeObjectURL(objectUrl);
  }, [blob]);
  return url;
}

export function useSetMyProfilePicture() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (file: File) => {
      const body = new FormData();
      body.append("file", file, file.name);
      const { error } = await apiClient.POST("/api/platform/my-profile/picture", {
        body: body as unknown as { file?: string },
        bodySerializer: (b) => b as unknown as FormData,
      });
      if (error) throw error;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: myProfileQueryKey }),
  });
}

export function useDeleteMyProfilePicture() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { error } = await apiClient.DELETE("/api/platform/my-profile/picture");
      if (error) throw error;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: myProfileQueryKey }),
  });
}

export function useChangeMyPassword() {
  return useMutation({
    mutationFn: async ({ currentPassword, newPassword }: { currentPassword: string; newPassword: string }) => {
      const { error } = await apiClient.POST("/api/account/my-profile/change-password", {
        body: { currentPassword, newPassword },
      });
      if (error) throw error;
    },
  });
}

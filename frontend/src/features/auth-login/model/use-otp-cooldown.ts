"use client";

import { useEffect, useRef, useState } from "react";

const COOLDOWN_SEC = 60;

export function useOtpCooldown() {
  const [cooldown, setCooldown] = useState(0);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
    };
  }, []);

  const start = () => {
    if (timerRef.current) clearInterval(timerRef.current);

    setCooldown(COOLDOWN_SEC);

    timerRef.current = setInterval(() => {
      setCooldown((prev) => {
        if (prev <= 1) {
          if (timerRef.current) clearInterval(timerRef.current);
          return 0;
        }
        return prev - 1;
      });
    }, 1000);
  };

  return {
    cooldown,
    isActive: cooldown > 0,
    start,
  };
}

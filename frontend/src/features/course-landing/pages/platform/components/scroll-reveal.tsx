"use client";

import { motion, type Variants } from "framer-motion";
import { useReducedMotion } from "../hooks/use-reduced-motion";

// ---------------------------------------------------------------------------
// Shared variants
// ---------------------------------------------------------------------------

const EASE = [0.25, 0.46, 0.45, 0.94] as const;

export const variants = {
  fadeUp: {
    hidden: { opacity: 0, y: 30 },
    visible: { opacity: 1, y: 0, transition: { duration: 0.6, ease: EASE } },
  } satisfies Variants,

  fadeIn: {
    hidden: { opacity: 0 },
    visible: { opacity: 1, transition: { duration: 0.6, ease: EASE } },
  } satisfies Variants,

  scaleIn: {
    hidden: { opacity: 0, scale: 0.95 },
    visible: { opacity: 1, scale: 1, transition: { duration: 0.6, ease: EASE } },
  } satisfies Variants,

  staggerContainer: {
    hidden: {},
    visible: { transition: { staggerChildren: 0.1 } },
  } satisfies Variants,

  staggerContainerFast: {
    hidden: {},
    visible: { transition: { staggerChildren: 0.06 } },
  } satisfies Variants,
};

// ---------------------------------------------------------------------------
// ScrollReveal component
// ---------------------------------------------------------------------------

type VariantName = keyof typeof variants;

interface ScrollRevealProps {
  children: React.ReactNode;
  variant?: VariantName;
  delay?: number;
  className?: string;
}

export function ScrollReveal({
  children,
  variant = "fadeUp",
  delay = 0,
  className,
}: ScrollRevealProps) {
  const reduced = useReducedMotion();
  const v = variants[variant];

  if (reduced) {
    return <div className={className}>{children}</div>;
  }

  return (
    <motion.div
      className={className}
      initial="hidden"
      whileInView="visible"
      viewport={{ once: true, margin: "-15%" }}
      variants={v}
      transition={delay > 0 ? { delay } : undefined}
    >
      {children}
    </motion.div>
  );
}

// ---------------------------------------------------------------------------
// StaggerContainer — parent that staggers children
// ---------------------------------------------------------------------------

interface StaggerContainerProps {
  children: React.ReactNode;
  className?: string;
  fast?: boolean;
}

export function StaggerContainer({ children, className, fast }: StaggerContainerProps) {
  const reduced = useReducedMotion();

  if (reduced) {
    return <div className={className}>{children}</div>;
  }

  return (
    <motion.div
      className={className}
      initial="hidden"
      whileInView="visible"
      viewport={{ once: true, margin: "-15%" }}
      variants={fast ? variants.staggerContainerFast : variants.staggerContainer}
    >
      {children}
    </motion.div>
  );
}

// ---------------------------------------------------------------------------
// StaggerItem — child within a StaggerContainer
// ---------------------------------------------------------------------------

interface StaggerItemProps {
  children: React.ReactNode;
  className?: string;
  variant?: VariantName;
}

export function StaggerItem({ children, className, variant = "fadeUp" }: StaggerItemProps) {
  const reduced = useReducedMotion();

  if (reduced) {
    return <div className={className}>{children}</div>;
  }

  return (
    <motion.div className={className} variants={variants[variant]}>
      {children}
    </motion.div>
  );
}

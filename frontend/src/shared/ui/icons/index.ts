/**
 * Central icon registry — the single source of truth for the platform.
 *
 * **Why this exists:**
 *   Before: 142 unique lucide imports across the codebase, with multiple icons
 *   used inconsistently for the same concept (Edit2 vs Edit3 vs Pencil for "edit",
 *   Lock vs LockKeyhole for "locked", AlertCircle vs AlertTriangle for "warning").
 *
 *   After: every concept has ONE canonical icon, named semantically. Same concept
 *   in admin panel, author tools, student view → identical icon, automatically.
 *
 * **Usage:**
 * ```tsx
 * import { Icons } from "@/shared/ui/icons";
 *
 * <Icons.edit size={14} className="text-muted-foreground" />
 * <Icons.lesson size={16} />        // entity icons (custom SVG marks)
 * <Icons.completed className="text-green" />
 * ```
 *
 * **Adding a new icon:**
 *   1. Pick a semantic key (e.g. `pin`, not `Pin`)
 *   2. Map it to a single canonical lucide icon (or custom mark)
 *   3. Add to the relevant section below
 *   4. Use `Icons.<key>` everywhere — never import lucide directly for that concept
 *
 * **Custom entity marks** live in `./entity-marks.tsx` and are wired into
 * `Icons.course/module/lesson/issue/article/project` automatically.
 */

import {
  // Analytics / admin
  BarChart3,
  Gauge,
  // Video artifacts
  Captions,
  ListVideo,
  NotebookText,
  // Status / feedback
  AlertCircle,
  AlertTriangle,
  Check,
  CheckCheck,
  CheckCircle2,
  Clock,
  Clock3,
  Loader2,
  MessageSquareWarning,
  ThumbsDown,
  ThumbsUp,
  // Navigation
  ArrowLeft,
  ArrowRight,
  ArrowRightLeft,
  ArrowUpRight,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  ChevronsUpDown,
  ExternalLink,
  Home,
  LayoutDashboard,
  Menu,
  PanelLeft,
  PanelLeftClose,
  PanelLeftOpen,
  Search,
  SearchX,
  // Actions / CRUD
  Archive,
  ArchiveRestore,
  Camera,
  Copy,
  Download,
  GripVertical,
  ImagePlus,
  MoreHorizontal,
  MoreVertical,
  Paperclip,
  Pencil,
  Plus,
  RefreshCw,
  Save,
  Send,
  Share,
  Trash2,
  Upload,
  X,
  // Auth / security
  Github,
  Key,
  KeyRound,
  Linkedin,
  LogIn,
  LogOut,
  Lock,
  LockKeyhole,
  Mail,
  Shield,
  ShieldAlert,
  ShieldCheck,
  Unlock,
  // Users / roles
  User,
  UserCog,
  UserPlus,
  Users,
  // Learning / playback
  Bookmark,
  BookmarkCheck,
  Compass,
  Eye,
  Film,
  Mic,
  MicOff,
  Play,
  PlayCircle,
  Square,
  Video,
  // Gamification
  Crown,
  Flame,
  Gift,
  Percent,
  Lightbulb,
  Medal,
  Rocket,
  Sparkles,
  Target,
  TrendingUp,
  Trophy,
  Zap,
  // Content / structure
  Bell,
  BellOff,
  CalendarDays,
  ClipboardCheck,
  ClipboardList,
  Database,
  FileArchive,
  FileCode2,
  FileImage,
  FileSpreadsheet,
  FilePlus2,
  FileText,
  LayoutGrid,
  Globe,
  HelpCircle,
  Layers,
  LayoutList,
  Link as LinkIcon,
  Link2,
  ListChecks,
  ListTree,
  Map,
  MessageSquare,
  Pin,
  Presentation,
  ScrollText,
  Server,
  StickyNote,
  Tags,
  Unlink,
  // Theme
  Moon,
  MonitorSmartphone,
  Sun,
  // Editor / markdown toolbar
  Bold,
  Code,
  Heading2,
  Italic,
  List,
  ListOrdered,
  NotebookPen,
  Quote,
  Radio,
  Strikethrough,
  // Misc
  Briefcase,
  GitPullRequest,
  GraduationCap,
  Maximize2,
  PenLine,
  Redo2,
  Settings2,
  Undo2,
  CreditCard,
} from "lucide-react";

import {
  ArticleMark,
  CourseMark,
  IssueMark,
  LessonMark,
  ModuleMark,
  ProjectMark,
} from "./entity-marks";

/**
 * Single source of truth for every icon used on the platform.
 *
 * Each key is a *concept* (semantic), not a lucide name. If you find yourself
 * thinking "I need a pencil icon" — stop, ask "what does it represent?", and
 * pick `edit` or `note` or `editor` accordingly.
 *
 * Categories below are visual grouping for humans only — at runtime this is
 * a single flat object. IntelliSense will show every key.
 */
export const Icons = {
  // ─────────────────────────────────────────────────────────────────────────
  // ENTITIES — core domain objects (custom SVG marks)
  // ─────────────────────────────────────────────────────────────────────────
  course: CourseMark,
  module: ModuleMark,
  lesson: LessonMark,
  issue: IssueMark,
  article: ArticleMark,
  project: ProjectMark,
  // Adjacent domain concepts (lucide)
  tag: Tags,
  comment: MessageSquare,
  bookmark: Bookmark,
  bookmarkFilled: BookmarkCheck,
  user: User,
  users: Users,
  userAdd: UserPlus,
  userSettings: UserCog,
  role: Shield,
  roadmap: Map,

  // ─────────────────────────────────────────────────────────────────────────
  // STATUS — feedback / state indicators
  // ─────────────────────────────────────────────────────────────────────────
  success: CheckCircle2,
  check: Check,
  creditCard: CreditCard,
  checkAll: CheckCheck,
  error: AlertCircle,
  warning: AlertTriangle,
  info: AlertCircle,
  help: HelpCircle,
  loading: Loader2,
  locked: Lock,
  lockedKey: LockKeyhole,
  unlocked: Unlock,
  thumbsUp: ThumbsUp,
  thumbsDown: ThumbsDown,

  // ─────────────────────────────────────────────────────────────────────────
  // PROGRESS — learning state (used by ItemProgressIndicator + curriculum)
  // ─────────────────────────────────────────────────────────────────────────
  completed: CheckCircle2,
  inProgress: Clock3,
  pending: Clock,
  reviewChangesRequested: MessageSquareWarning,
  reviewSubmitted: GitPullRequest,

  // ─────────────────────────────────────────────────────────────────────────
  // NAVIGATION — page-level navigation, sidebar, top bar
  // ─────────────────────────────────────────────────────────────────────────
  home: Home,
  dashboard: LayoutDashboard,
  menu: Menu,
  back: ArrowLeft,
  forward: ArrowRight,
  search: Search,
  searchEmpty: SearchX,
  sidebarToggle: PanelLeft,
  theaterMode: PanelLeftClose,
  theaterModeExit: PanelLeftOpen,
  externalLink: ExternalLink,

  // ─────────────────────────────────────────────────────────────────────────
  // DIRECTION — arrows, chevrons, flow
  // ─────────────────────────────────────────────────────────────────────────
  arrowLeft: ArrowLeft,
  arrowRight: ArrowRight,
  arrowUpRight: ArrowUpRight,
  arrowSwap: ArrowRightLeft,
  chevronUp: ChevronUp,
  chevronDown: ChevronDown,
  chevronLeft: ChevronLeft,
  chevronRight: ChevronRight,
  chevronsUpDown: ChevronsUpDown,

  // ─────────────────────────────────────────────────────────────────────────
  // ACTIONS — CRUD, manipulation, common buttons
  // ─────────────────────────────────────────────────────────────────────────
  add: Plus,
  edit: Pencil,
  editAlt: PenLine,
  delete: Trash2,
  save: Save,
  copy: Copy,
  upload: Upload,
  shareIos: Share,
  send: Send,
  refresh: RefreshCw,
  archive: Archive,
  restore: ArchiveRestore,
  close: X,
  more: MoreHorizontal,
  moreVertical: MoreVertical,
  drag: GripVertical,
  undo: Undo2,
  redo: Redo2,
  expand: Maximize2,
  settings: Settings2,
  uploadImage: ImagePlus,
  uploadFile: FilePlus2,
  capture: Camera,
  paperclip: Paperclip,
  download: Download,

  // ─────────────────────────────────────────────────────────────────────────
  // AUTH / SECURITY — login, providers, keys, shields
  // ─────────────────────────────────────────────────────────────────────────
  login: LogIn,
  logout: LogOut,
  github: Github,
  linkedin: Linkedin,
  telegram: Send,
  mail: Mail,
  password: Key,
  passwordSecure: KeyRound,
  shield: Shield,
  shieldAlert: ShieldAlert,
  shieldCheck: ShieldCheck,

  // ─────────────────────────────────────────────────────────────────────────
  // PLAYBACK / MEDIA
  // ─────────────────────────────────────────────────────────────────────────
  play: Play,
  playCircle: PlayCircle,
  video: Video,
  film: Film,
  view: Eye,
  note: NotebookPen,
  stream: Radio,
  mic: Mic,
  micOff: MicOff,
  stop: Square,

  // Video artifact badges (course-builder)
  transcript: Captions,
  timecodes: ListVideo,
  summary: NotebookText,

  // ─────────────────────────────────────────────────────────────────────────
  // GAMIFICATION — XP, levels, achievements
  // ─────────────────────────────────────────────────────────────────────────
  trophy: Trophy,
  medal: Medal,
  crown: Crown,
  gift: Gift,
  percent: Percent,
  streak: Flame,
  xp: Sparkles,
  ai: Sparkles,
  energy: Zap,
  target: Target,
  rocket: Rocket,
  lightbulb: Lightbulb,
  trending: TrendingUp,

  // ─────────────────────────────────────────────────────────────────────────
  // COMMUNICATION — messaging, notifications
  // ─────────────────────────────────────────────────────────────────────────
  notification: Bell,
  notificationOff: BellOff,
  message: MessageSquare,

  // ─────────────────────────────────────────────────────────────────────────
  // CONTENT / STRUCTURE
  // ─────────────────────────────────────────────────────────────────────────
  link: LinkIcon,
  attachment: Link2,
  unlink: Unlink,
  library: Layers,
  layers: Layers,
  list: LayoutList,
  chart: BarChart3,
  levelTest: Gauge,
  listChecks: ListChecks,
  listTree: ListTree,
  quiz: ClipboardCheck,
  clipboard: ClipboardList,
  clipboardCheck: ClipboardCheck,
  document: FileText,
  fileArchive: FileArchive,
  fileCode: FileCode2,
  fileImage: FileImage,
  fileSheet: FileSpreadsheet,
  grid: LayoutGrid,
  scroll: ScrollText,
  pin: Pin,
  stickyNote: StickyNote,
  presentation: Presentation,
  briefcase: Briefcase,
  graduation: GraduationCap,
  compass: Compass,
  database: Database,
  server: Server,
  globe: Globe,

  // ─────────────────────────────────────────────────────────────────────────
  // TIME
  // ─────────────────────────────────────────────────────────────────────────
  clock: Clock,
  calendar: CalendarDays,

  // ─────────────────────────────────────────────────────────────────────────
  // THEME
  // ─────────────────────────────────────────────────────────────────────────
  themeLight: Sun,
  themeDark: Moon,
  themeSystem: MonitorSmartphone,

  // ─────────────────────────────────────────────────────────────────────────
  // EDITOR — markdown / rich text toolbar
  // ─────────────────────────────────────────────────────────────────────────
  bold: Bold,
  italic: Italic,
  strikethrough: Strikethrough,
  inlineCode: Code,
  quote: Quote,
  heading: Heading2,
  bulletList: List,
  orderedList: ListOrdered,
} as const;

/**
 * Type representing a single icon component from the registry.
 * Useful when passing icons as props.
 *
 * @example
 *   interface CardProps { icon: IconComponent; }
 *   <Card icon={Icons.edit} />
 */
export type IconComponent = (typeof Icons)[keyof typeof Icons];

/**
 * Convenience: re-export the custom entity marks for code that explicitly
 * needs the SVG primitives (e.g. inside other custom icons).
 */
export {
  ArticleMark,
  CourseMark,
  IssueMark,
  LessonMark,
  ModuleMark,
  ProjectMark,
} from "./entity-marks";

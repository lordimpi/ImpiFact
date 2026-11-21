/**
 * One step of a page breadcrumb (`ui-page-title [breadcrumb]`). Every step links to its page except the last one,
 * which is the current page.
 */
export interface UiBreadcrumbItem {
  /** Text of the step (already translated). */
  label: string;
  /** Router link of the step; the last step (the current page) has none. */
  link?: string;
}

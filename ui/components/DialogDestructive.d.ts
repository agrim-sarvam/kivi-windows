import * as React from 'react';
export interface DialogDestructiveProps {
  className?: string;
  style?: React.CSSProperties;
  /** Text content; defaults to "delete \"work messaging\"?". */
  text1?: string;
  /** Text content; defaults to "The persona and its 3 custom rules will be removed. Apps assigned to it fall back to casual. This can’t be undone.". */
  text2?: string;
  /** Swappable nested instance; defaults to the design's. */
  icon1?: React.ReactNode;
}
export declare const DialogDestructive: React.FC<DialogDestructiveProps>;
export default DialogDestructive;

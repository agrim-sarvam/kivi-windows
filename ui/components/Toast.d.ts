import * as React from 'react';
export interface ToastProps {
  className?: string;
  style?: React.CSSProperties;
  /** Text content; defaults to "Pasted at cursor · Slack". */
  text1?: string;
}
export declare const Toast: React.FC<ToastProps>;
export default Toast;

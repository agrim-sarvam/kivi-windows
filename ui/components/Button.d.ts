import * as React from 'react';
export interface ButtonProps {
  className?: string;
  style?: React.CSSProperties;
  kind?: "primary" | "secondary" | "ghost" | "danger";
  size?: "md" | "sm";
  /** Text content; defaults to "dictate". */
  text1?: string;
  /** Text content; defaults to "R Ctrl". */
  text2?: string;
}
export declare const Button: React.FC<ButtonProps>;
export default Button;

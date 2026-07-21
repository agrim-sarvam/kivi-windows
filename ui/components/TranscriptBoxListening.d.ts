import * as React from 'react';
export interface TranscriptBoxListeningProps {
  className?: string;
  style?: React.CSSProperties;
  /** Text content; defaults to "LIVE". */
  text1?: string;
  /** Text content; defaults to "hi-IN · auto". */
  text2?: string;
  /** Text content; defaults to "Press right ctrl and speak — finished text appears here, in your style…". */
  text3?: string;
  /** Text content; defaults to "right ctrl to stop · esc to discard". */
  text4?: string;
}
export declare const TranscriptBoxListening: React.FC<TranscriptBoxListeningProps>;
export default TranscriptBoxListening;

import * as React from 'react';
export interface TranscriptBoxHeyKiviProps {
  className?: string;
  style?: React.CSSProperties;
  /** Text content; defaults to "HEY KIVI · \"MAKE IT FORMAL\"". */
  text1?: string;
  /** Text content; defaults to "hi-IN · auto". */
  text2?: string;
  /** Text content; defaults to "Kal 3 PM works — see you then. Confirming tomorrow at 3 PM. I’ll share the doc beforehand.". */
  text3?: string;
  /** Text content; defaults to "⏎ paste · esc keep original". */
  text4?: string;
}
export declare const TranscriptBoxHeyKivi: React.FC<TranscriptBoxHeyKiviProps>;
export default TranscriptBoxHeyKivi;

import { Component, input, output } from '@angular/core';

/** The AI mark: a large and a small four-pointed sparkle with two dots. Takes the current text colour and size. */
@Component({
  selector: 'app-ai-icon',
  template: `
    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" aria-hidden="true">
      <path d="M10 6.5Q10 14 17.5 14Q10 14 10 21.5Q10 14 2.5 14Q10 14 10 6.5Z" />
      <path d="M17 2.5Q17 6 20.5 6Q17 6 17 9.5Q17 6 13.5 6Q17 6 17 2.5Z" />
      <circle cx="5.5" cy="5.5" r="1.1" />
      <circle cx="19.5" cy="18.5" r="1.1" />
    </svg>
  `,
  styles: `
    :host { display: inline-flex; vertical-align: -0.125em; }
  `,
})
export class AiIcon {}

/**
 * The icon-only "AI แนะนำ" button that sits inside an input (as its nzSuffix). The hint says what it does, or why
 * it is off; it is on the host so it still shows while the button is disabled.
 */
@Component({
  selector: 'app-ai-suggest',
  imports: [AiIcon],
  host: { '[attr.title]': 'hint()' },
  template: `
    <button
      type="button"
      class="ai"
      [class.busy]="busy()"
      [disabled]="disabled() || busy()"
      [attr.aria-label]="hint()"
      [attr.aria-busy]="busy()"
      (click)="pressed.emit()"
    >
      <app-ai-icon />
    </button>
  `,
  styles: `
    :host { display: inline-flex; }
    .ai {
      display: grid;
      place-items: center;
      padding: 0 2px;
      border: 0;
      background: transparent;
      color: var(--accent);
      font-size: 16px;
      line-height: 1;
      cursor: pointer;
    }
    .ai:hover:not(:disabled) { opacity: 0.75; }
    .ai:disabled { color: var(--ink-3); cursor: not-allowed; opacity: 0.5; }
    .ai.busy { color: var(--accent); cursor: progress; opacity: 1; }
    .ai.busy app-ai-icon { animation: twinkle 0.9s ease-in-out infinite; }
    @keyframes twinkle {
      50% { opacity: 0.35; transform: scale(0.85); }
    }
    @media (prefers-reduced-motion: reduce) {
      .ai.busy app-ai-icon { animation: none; opacity: 0.5; }
    }
  `,
})
export class AiSuggest {
  readonly busy = input(false);
  readonly disabled = input(false);
  /** Tooltip and accessible name. */
  readonly hint = input('AI แนะนำ');
  readonly pressed = output<void>();
}

<script lang="ts">
  import type { Snippet } from 'svelte';

  // primary: chiaro su scuro (azione principale); secondary: bordo; ghost: solo testo; soft: velo chiaro
  type Variant = 'primary' | 'secondary' | 'ghost' | 'soft';
  let {
    variant = 'secondary',
    size = 'm',
    type = 'button',
    disabled = false,
    pressed = undefined,
    href = undefined,
    external = false,
    label = undefined,
    onclick = undefined,
    children,
  }: {
    variant?: Variant;
    size?: 's' | 'm' | 'l';
    type?: 'button' | 'submit';
    disabled?: boolean;
    pressed?: boolean;
    href?: string;
    /** link fuori dall'app (trailer...): si apre in una nuova scheda del browser (26/11/2026, Antonio) */
    external?: boolean;
    label?: string;
    onclick?: (e: MouseEvent) => void;
    children: Snippet;
  } = $props();
</script>

{#if href}
  <a class="btn {variant} {size}" {href} aria-label={label} {onclick} target={external ? '_blank' : undefined} rel={external ? 'noopener noreferrer' : undefined}>{@render children()}</a>
{:else}
  <button class="btn {variant} {size}" {type} {disabled} aria-pressed={pressed} aria-label={label} {onclick}>{@render children()}</button>
{/if}

<style>
  .btn {
    display: inline-flex; align-items: center; justify-content: center; gap: 8px;
    border-radius: var(--radius); border: 1px solid transparent;
    font-weight: 600; text-decoration: none; cursor: pointer; white-space: nowrap;
  }
  .s { min-height: 40px; padding: 0 14px; font-size: 14px; }
  .m { min-height: 44px; padding: 0 18px; font-size: 15px; }
  .l { min-height: 48px; padding: 0 22px; font-size: 16px; }
  .primary { background: var(--text); color: var(--bg); font-weight: 700; }
  .primary:hover { background: #ffffff; color: var(--bg); }
  .secondary { background: none; border-color: var(--line-btn); color: var(--text); }
  .secondary:hover { background: var(--raised); color: var(--text); }
  .soft { background: rgba(242, 244, 247, 0.14); color: var(--text); }
  .soft:hover { background: rgba(242, 244, 247, 0.2); color: var(--text); }
  .ghost { background: none; color: var(--muted); font-weight: 500; }
  .ghost:hover { color: var(--text); }
  .btn:disabled { background: var(--raised); color: var(--placeholder); border-color: transparent; cursor: not-allowed; }
</style>

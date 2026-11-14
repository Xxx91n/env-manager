<script lang="ts">
  import { frontendLog } from '../settingsStore'
  type ComboItem = { name: string; value?: string }

  // Reusable searchable combobox pattern, extracted from the v0.7.4 clone-from-existing
  // picker in ProfilePage.svelte. The native <select> dropdown is OS-controlled so
  // typing into a separate search input above it had no visual feedback (the option
  // list did not visibly re-filter inline). This self-rendered combobox solves that:
  // the <input> + <ul> dropdown re-filters on every keystroke. Used by ProfilePage's
  

  // When the caller sets 'selectedAfter', callers may pass a boolean true to indicate
  // that selection has happened and the combobox should visually collapse into
  // a compact one-line state, freeing the screen for the next-step inputs.
  
  // When true, the input keeps the selected item's name visible after selection
  // (used by PATH entries where the user needs to see the chosen path in the input).
  // When false, the input clears on select (used by add-var where selected values
  
  interface Props {
    // add-var / add-path panels and ProtectionPage's protected-var / protected-path pickers.
    items?: ComboItem[];
    placeholder?: string;
    label?: string;
    // Default false keeps the always-open pattern.
    collapseAfterSelect?: boolean;
    // flow into separate name/value inputs below).
    keepQueryOnSelect?: boolean;
    oninput?: (value: string) => void;
    onselect?: (item: ComboItem) => void;
  }

  let {
    items = [] = [],
    placeholder = '',
    label = '',
    collapseAfterSelect = true,
    keepQueryOnSelect = false,
    oninput,
    onselect
  }: Props = $props();

  let query = $state('')
  let dropdownOpen = $state(false)
  let highlightIndex = $state(-1)

  let filtered = $derived(query.trim()
    ? items.filter(v => v.name.toLowerCase().includes(query.toLowerCase()))
    : items)

  function onInput(e) {
    highlightIndex = -1
    dropdownOpen = true
    // Svelte 5: callback props (oninput/onselect) replace createEventDispatcher.
    // Read the current DOM value from the event target to avoid dispatching
    // a stale value (one keystroke behind).
    const current = (e.target as HTMLInputElement).value
    oninput?.(current)
  }

  function select(v: ComboItem) {
    onselect?.(v)
    if (keepQueryOnSelect) {
      // PATH mode: show the selected path name in the input so the user
      // sees their selection. The parent already set newPathEntry from the
      // select event detail.
      query = v.name
    } else {
      // Add-var mode: clear the search so a new search can start immediately.
      // The selected name/value flows into separate inputs below.
      query = ''
    }
    dropdownOpen = false
    highlightIndex = -1
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (filtered.length) {
        dropdownOpen = true
        highlightIndex = Math.min(highlightIndex + 1, Math.min(filtered.length, 10) - 1)
      }
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      highlightIndex = Math.max(highlightIndex - 1, 0)
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const v = filtered[highlightIndex >= 0 ? highlightIndex : 0]
      if (v) select(v)
    } else if (e.key === 'Escape') {
      dropdownOpen = false
    }
  }
</script>

<div class="relative">
  {#if label}
    <label class="block text-[10px] font-medium text-muted-foreground mb-0.5">{label}</label>
  {/if}
  <input
    type="text"
    placeholder={placeholder}
    bind:value={query}
    oninput={onInput}
    onfocus={() => { dropdownOpen = true }}
    onkeydown={onKeydown}
    onblur={() => { setTimeout(() => { dropdownOpen = false }, 150) }}
    class="w-full px-2 py-1 text-[10px] border border-border rounded focus:outline-hidden focus:ring-1 focus:ring-primary bg-accent border-border/80 text-foreground"
  />
  {#if dropdownOpen && filtered.length > 0}
    <ul class="absolute z-30 left-0 right-0 mt-0.5 bg-card border border-border border-border/80 rounded shadow-lg max-h-40 overflow-auto">
      {#each filtered.slice(0, 10) as v, i (v.name)}
        <li
          class="px-2 py-1 cursor-pointer text-[10px] flex items-center gap-2 {i === highlightIndex ? 'bg-primary/10 bg-primary/20/40' : 'hover:bg-muted/30 hover:bg-accent'}"
          onmousedown={(e) => { e.preventDefault(); select(v) }}
          role="option"
          aria-selected={i === highlightIndex}
        >
          <span class="font-mono text-foreground/80 text-foreground truncate flex-1">{v.name}</span>
          <span class="font-mono text-[9px] text-muted-foreground truncate max-w-[40%]">{v.value}</span>
        </li>
      {/each}
    </ul>
  {/if}
</div>

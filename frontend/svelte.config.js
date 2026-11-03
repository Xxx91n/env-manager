// Svelte 5 migration: svelte-preprocess dropped.
// vitePreprocess (from vite-plugin-svelte) handles <script lang="ts"> and
// <style lang="postcss"> via the Vite pipeline (postcss.config.js is picked up by Vite).
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte'

export default {
	preprocess: vitePreprocess()
}

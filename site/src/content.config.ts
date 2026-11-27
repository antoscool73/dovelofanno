import { defineCollection } from 'astro:content';
import { docsLoader, i18nLoader } from '@astrojs/starlight/loaders';
import { docsSchema, i18nSchema } from '@astrojs/starlight/schema';

export const collections = {
  docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }),
  // frasi in italiano che Starlight non ha (la ricerca Pagefind era in inglese)
  i18n: defineCollection({ loader: i18nLoader(), schema: i18nSchema() }),
};

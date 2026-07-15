// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import tailwindcss from '@tailwindcss/vite';

import cloudflare from '@astrojs/cloudflare';

export default defineConfig({
  site: 'https://dotcarbon.dev',

  integrations: [
    starlight({
      title: 'DotCarbon',
      description: 'Build native desktop and mobile apps with C#, .NET, and any Vite frontend.',
      logo: {
        src: './src/assets/houston.webp',
        alt: 'DotCarbon',
      },
      favicon: '/favicon.svg',
      customCss: ['./src/styles/global.css'],
      lastUpdated: true,
      pagination: true,
      social: [
        {
          icon: 'github',
          label: 'GitHub',
          href: 'https://github.com/DanKaufmanDev/dotcarbon',
        },
      ],
      editLink: {
        baseUrl: 'https://github.com/DanKaufmanDev/dotcarbon/edit/main/dotcarbon-site/',
      },
      expressiveCode: {
        themes: ['github-dark-default', 'github-light'],
        styleOverrides: {
          borderRadius: '0.375rem',
          borderColor: 'var(--sl-color-hairline-shade)',
        },
      },
      sidebar: [
        {
          label: 'Start',
          items: [
            { label: 'Overview', slug: 'index' },
            { label: 'Prerequisites', slug: 'start/prerequisites' },
            { label: 'Create a project', slug: 'start/create-project' },
            { label: 'Project structure', slug: 'start/project-structure' },
          ],
        },
        {
          label: 'Learn',
          items: [
            { label: 'Architecture', slug: 'learn/architecture' },
            { label: 'Commands', slug: 'learn/commands' },
            { label: 'Type generation', slug: 'learn/type-generation' },
            { label: 'State & dependency injection', slug: 'learn/state-and-di' },
            { label: 'Events', slug: 'learn/events' },
            { label: 'Windows & webviews', slug: 'learn/windows' },
            { label: 'Lifecycle', slug: 'learn/lifecycle' },
            { label: 'Tray & menus', slug: 'learn/tray-and-menus' },
            { label: 'Using NuGet packages', slug: 'learn/nuget' },
          ],
        },
        {
          label: 'Security',
          items: [
            { label: 'Security model', slug: 'security/overview' },
            { label: 'Capabilities', slug: 'security/capabilities' },
            { label: 'Plugin scopes', slug: 'security/plugin-scopes' },
            { label: 'Content Security Policy', slug: 'security/csp' },
          ],
        },
        {
          label: 'Develop',
          items: [
            { label: 'Development workflow', slug: 'develop/workflow' },
            { label: 'Configuration', slug: 'develop/configuration' },
            { label: 'Icons', slug: 'develop/icons' },
            { label: 'Platform projects', slug: 'develop/platforms' },
            { label: 'Android', slug: 'develop/android' },
            { label: 'iOS', slug: 'develop/ios' },
            { label: 'Diagnostics', slug: 'develop/diagnostics' },
          ],
        },
        {
          label: 'Plugins',
          items: [
            { label: 'Plugin system', slug: 'plugins/overview' },
            { label: 'Author a plugin', slug: 'plugins/authoring' },
            { label: 'Clipboard', slug: 'plugins/clipboard' },
            { label: 'Deep Link', slug: 'plugins/deep-link' },
            { label: 'Dialog', slug: 'plugins/dialog' },
            { label: 'File System', slug: 'plugins/file-system' },
            { label: 'Global Shortcut', slug: 'plugins/global-shortcut' },
            { label: 'HTTP', slug: 'plugins/http' },
            { label: 'Notification', slug: 'plugins/notification' },
            { label: 'Opener', slug: 'plugins/opener' },
            { label: 'OS', slug: 'plugins/os' },
            { label: 'Shell', slug: 'plugins/shell' },
            { label: 'Single Instance', slug: 'plugins/single-instance' },
            { label: 'Store', slug: 'plugins/store' },
            { label: 'Updater', slug: 'plugins/updater' },
            { label: 'Window', slug: 'plugins/window' },
          ],
        },
        {
          label: 'Distribute',
          items: [
            { label: 'Build for production', slug: 'distribute/building' },
            { label: 'Desktop packages', slug: 'distribute/desktop' },
            { label: 'Android packages', slug: 'distribute/android' },
            { label: 'iOS packages', slug: 'distribute/ios' },
            { label: 'Signing & notarization', slug: 'distribute/signing' },
            { label: 'Updater artifacts', slug: 'distribute/updater' },
            { label: 'Continuous integration', slug: 'distribute/ci' },
          ],
        },
        {
          label: 'Reference',
          items: [
            { label: 'CLI', slug: 'reference/cli' },
            { label: 'carbon.json', slug: 'reference/configuration' },
            { label: 'Frontend API', slug: 'reference/frontend-api' },
            { label: 'Packages', slug: 'reference/packages' },
            { label: 'Platform support', slug: 'reference/platform-support' },
          ],
        },
      ],
    }),
  ],

  vite: {
    plugins: [tailwindcss()],
  },

  adapter: cloudflare(),
});
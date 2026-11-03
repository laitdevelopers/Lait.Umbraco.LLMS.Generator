// Browsers cache plugin files for 24 hours (Cache-Control max-age=86400) and Umbraco loads this file as a dynamic import,
// so even a hard refresh can keep running an old copy. Bump the ?v= here and in umbraco-package.json whenever a file changes.
import { LitElement, html, css, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { UmbSorterController } from '@umbraco-cms/backoffice/sorter';

const BASE = '/umbraco/lait-llms/api/v1';
const TYPES = ['text', 'heading', 'link', 'section'];
const ICONS = { text: 'icon-paragraph', heading: 'icon-heading', link: 'icon-link', section: 'icon-tree' };

const newBlock = (type) => ({
  id: crypto.randomUUID(), type, text: '', node: null, url: '', description: '',
  includeDescendants: false, maxDepth: null, maxPages: null, includeTypes: [], excludeTypes: [], skipHidden: true,
});
const list = (s) => s.split(',').map((x) => x.trim()).filter(Boolean);
const keys = (v) => (v ? String(v).split(',').filter(Boolean) : []);
const intOrNull = (v) => (String(v).trim() === '' ? null : Number(v));

class LaitLlmsDashboard extends UmbElementMixin(LitElement) {
  static properties = {
    _sites: { state: true },
    _site: { state: true },
    _config: { state: true },
    _preview: { state: true },
    _errors: { state: true },
    _status: { state: true },
    _busy: { state: true },
  };

  #auth;
  #previewTimer;
  #sorter = new UmbSorterController(this, {
    getUniqueOfElement: (el) => el.dataset.id,
    getUniqueOfModel: (b) => b.id,
    identifier: 'lait-llms-blocks',
    itemSelector: '.block',
    containerSelector: '.blocks',
    handleSelector: '.handle',
    onChange: ({ model }) => this.#set({ blocks: model }),
  });

  constructor() {
    super();
    this._errors = [];
    this.consumeContext(UMB_AUTH_CONTEXT, (auth) => {
      this.#auth = auth;
      this.#loadSites();
    });
  }

  async #fetch(path, options = {}) {
    const token = await this.#auth?.getLatestToken();
    return fetch(BASE + path, {
      ...options,
      headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    });
  }

  async #loadSites() {
    try {
      const response = await this.#fetch('/sites');
      if (!response.ok) throw new Error();
      this._sites = await response.json();
      if (this._sites.length) await this.#loadConfig(this._sites[0].key);
    } catch {
      this._errors = [this.localize.term('laitLlms_loadFailed')];
    }
  }

  async #loadConfig(key) {
    this._site = key;
    this._errors = [];
    this._status = null;
    const response = await this.#fetch(`/config?root=${key}`);
    if (!response.ok) return (this._errors = [this.localize.term('laitLlms_loadFailed')]);
    const config = await response.json();
    for (const b of config.blocks) b.id ||= crypto.randomUUID();
    this._config = config;
    this.#sorter.setModel(config.blocks);
    this.#refreshPreview(0);
  }

  #set(patch) {
    this._config = { ...this._config, ...patch };
    this._status = null;
    if (patch.blocks) this.#sorter.setModel(this._config.blocks);
    this.#refreshPreview();
  }

  #setBlock(id, patch) {
    this.#set({ blocks: this._config.blocks.map((b) => (b.id === id ? { ...b, ...patch } : b)) });
  }

  #refreshPreview(delay = 400) {
    clearTimeout(this.#previewTimer);
    this.#previewTimer = setTimeout(async () => {
      const response = await this.#fetch(`/preview?root=${this._site}`, { method: 'POST', body: JSON.stringify(this._config) });
      if (response.ok) this._preview = await response.text();
    }, delay);
  }

  async #save() {
    this._busy = true;
    this._errors = [];
    this._status = null;
    try {
      const response = await this.#fetch(`/config?root=${this._site}`, { method: 'PUT', body: JSON.stringify(this._config) });
      if (response.status === 400) {
        this._errors = (await response.json()).map((e) =>
          (e.block ? `${this.localize.term('laitLlms_block')} ${e.block}: ` : '') + this.localize.term(e.term));
      } else if (!response.ok) {
        throw new Error();
      } else {
        this._status = this.localize.term('laitLlms_saved');
        this._sites = this._sites.map((s) => (s.key === this._site ? { ...s, configured: true } : s));
      }
    } catch {
      this._errors = [this.localize.term('laitLlms_saveFailed')];
    } finally {
      this._busy = false;
    }
  }

  render() {
    if (!this._sites) return html`<uui-loader></uui-loader>${this.#renderErrors()}`;
    if (!this._sites.length) return html`<uui-box>${this.localize.term('laitLlms_noSites')}</uui-box>`;
    const site = this._sites.find((s) => s.key === this._site);
    const t = (k) => this.localize.term(`laitLlms_${k}`);

    return html`
      <p class="intro">${t('intro')}</p>
      <div class="top">
        ${this._sites.length > 1
          ? html`<uui-select label=${t('site')} .options=${this._sites.map((s) => ({ name: s.name, value: s.key, selected: s.key === this._site }))}
              @change=${(e) => this.#loadConfig(e.target.value)}></uui-select>`
          : html`<strong>${site?.name}</strong>`}
        <span class="live">${site?.configured ? this.#renderLive(site) : t('notLive')}</span>
      </div>
      ${this._config ? this.#renderEditor(t) : nothing}
    `;
  }

  #renderLive(site) {
    const t = (k) => this.localize.term(`laitLlms_${k}`);
    if (!site.domains.length) return html`${t('live')} <a href="/llms.txt" target="_blank">/llms.txt</a> ${t('noDomain')}`;
    const urls = [...new Set(site.domains.map((d) => {
      const u = new URL(d.includes('://') ? d : `https://${d}`);
      return `${u.origin}/llms.txt`;
    }))];
    return html`${t('live')} ${urls.map((u, i) => html`${i ? ', ' : ''}<a href=${u} target="_blank">${u}</a>`)}`;
  }

  #renderEditor(t) {
    const c = this._config;
    return html`
      <div class="grid">
        <div class="editor">
          <uui-box headline=${t('file')}>
            <umb-property-layout label=${t('title')} description=${t('titleHelp')} orientation="vertical">
              <uui-input slot="editor" .value=${c.title} @input=${(e) => this.#set({ title: e.target.value })} label=${t('title')}></uui-input>
            </umb-property-layout>
            <umb-property-layout label=${t('summary')} description=${t('summaryHelp')} orientation="vertical">
              <uui-textarea slot="editor" .value=${c.summary} @input=${(e) => this.#set({ summary: e.target.value })} label=${t('summary')}></uui-textarea>
            </umb-property-layout>
          </uui-box>

          <uui-box headline=${t('blocks')}>
            <p class="help">${t('blocksHelp')}</p>
            <div class="blocks">
              ${c.blocks.map((b) => this.#renderBlock(b, t))}
            </div>
            ${c.blocks.length ? nothing : html`<p class="help">${t('empty')}</p>`}
            <div class="add">
              ${TYPES.map((type) => html`
                <uui-button look="placeholder" label=${t(`type_${type}`)} @click=${() => this.#set({ blocks: [...c.blocks, newBlock(type)] })}>
                  <uui-icon name=${ICONS[type]}></uui-icon> ${t('add')}: ${t(`type_${type}`)}
                </uui-button>`)}
            </div>
          </uui-box>

          <uui-box headline=${t('advanced')}>
            <umb-property-layout label=${t('descriptionAliases')} description=${t('descriptionAliasesHelp')} orientation="vertical">
              <uui-input slot="editor" .value=${c.descriptionAliases.join(', ')} label=${t('descriptionAliases')}
                @change=${(e) => this.#set({ descriptionAliases: list(e.target.value) })}></uui-input>
            </umb-property-layout>
            <umb-property-layout label=${t('hiddenAliases')} description=${t('hiddenAliasesHelp')} orientation="vertical">
              <uui-input slot="editor" .value=${c.hiddenAliases.join(', ')} label=${t('hiddenAliases')}
                @change=${(e) => this.#set({ hiddenAliases: list(e.target.value) })}></uui-input>
            </umb-property-layout>
          </uui-box>

          ${this.#renderErrors()}
          <div class="actions">
            ${this._status ? html`<span class="ok">${this._status}</span>` : nothing}
            <uui-button look="primary" color="positive" label=${t('save')} ?disabled=${this._busy} .state=${this._busy ? 'waiting' : undefined}
              @click=${this.#save}></uui-button>
          </div>
        </div>

        <uui-box class="preview" headline=${t('preview')}>
          <p class="help">${t('previewHelp')}</p>
          <pre>${this._preview ?? ''}</pre>
        </uui-box>
      </div>
    `;
  }

  #renderBlock(b, t) {
    const set = (patch) => this.#setBlock(b.id, patch);
    const input = (key, label, placeholder = '') => html`
      <label>${label}
        <uui-input .value=${b[key] ?? ''} placeholder=${placeholder} label=${label} @input=${(e) => set({ [key]: e.target.value })}></uui-input>
      </label>`;

    return html`
      <div class="block" data-id=${b.id}>
        <div class="block-head">
          <uui-icon class="handle" name="icon-navigation" title=${t('drag')}></uui-icon>
          <uui-icon name=${ICONS[b.type]}></uui-icon>
          <strong>${t(`type_${b.type}`)}</strong>
          <span class="help">${t(`help_${b.type}`)}</span>
          <uui-button compact label=${t('remove')} @click=${() => this.#set({ blocks: this._config.blocks.filter((x) => x.id !== b.id) })}>
            <uui-icon name="icon-trash"></uui-icon>
          </uui-button>
        </div>
        <div class="block-body">
          ${b.type === 'text' ? html`
            <uui-textarea .value=${b.text} label=${t('text')} auto-height @input=${(e) => set({ text: e.target.value })}></uui-textarea>` : nothing}
          ${b.type === 'heading' ? input('text', t('heading')) : nothing}
          ${b.type === 'link' ? html`
            <label>${t('page')}
              <umb-input-document max="1" .value=${b.node ?? ''} @change=${(e) => set({ node: keys(e.target.value)[0] ?? null })}></umb-input-document>
            </label>
            ${b.node ? nothing : html`<span class="help">${t('or')}</span>${input('url', t('url'), 'https://')}`}
            ${input('text', t('linkTitle'), b.node ? t('linkTitlePlaceholder') : '')}
            ${input('description', t('description'), b.node ? t('descriptionPlaceholder') : '')}` : nothing}
          ${b.type === 'section' ? html`
            <label>${t('node')}
              <umb-input-document max="1" .value=${b.node ?? ''} @change=${(e) => set({ node: keys(e.target.value)[0] ?? null })}></umb-input-document>
            </label>
            ${input('text', t('sectionHeading'), t('sectionHeadingPlaceholder'))}
            <uui-toggle .checked=${b.includeDescendants} label=${t('includeDescendants')}
              @change=${(e) => set({ includeDescendants: e.target.checked })}></uui-toggle>
            <div class="row">
              ${b.includeDescendants ? html`
                <label>${t('maxDepth')}
                  <uui-input type="number" min="1" .value=${b.maxDepth ?? ''} placeholder=${t('noLimit')} label=${t('maxDepth')}
                    @input=${(e) => set({ maxDepth: intOrNull(e.target.value) })}></uui-input>
                </label>` : nothing}
              <label>${t('maxPages')}
                <uui-input type="number" min="1" .value=${b.maxPages ?? ''} placeholder=${t('noLimit')} label=${t('maxPages')}
                  @input=${(e) => set({ maxPages: intOrNull(e.target.value) })}></uui-input>
              </label>
            </div>
            <uui-toggle .checked=${b.skipHidden} label=${t('skipHidden')} @change=${(e) => set({ skipHidden: e.target.checked })}></uui-toggle>
            <details ?open=${b.includeTypes.length || b.excludeTypes.length}>
              <summary>${t('includeTypes')} / ${t('excludeTypes')}</summary>
              <p class="help">${t('typesHelp')}</p>
              <label>${t('includeTypes')}
                <umb-input-document-type .documentTypesOnly=${true} .value=${b.includeTypes.join(',')}
                  @change=${(e) => set({ includeTypes: keys(e.target.value) })}></umb-input-document-type>
              </label>
              <label>${t('excludeTypes')}
                <umb-input-document-type .documentTypesOnly=${true} .value=${b.excludeTypes.join(',')}
                  @change=${(e) => set({ excludeTypes: keys(e.target.value) })}></umb-input-document-type>
              </label>
            </details>` : nothing}
        </div>
      </div>
    `;
  }

  #renderErrors() {
    return this._errors.length
      ? html`<div class="errors">${this._errors.map((e) => html`<div>${e}</div>`)}</div>`
      : nothing;
  }

  static styles = css`
    :host { display: block; padding: var(--uui-size-layout-1); }
    .intro { margin-top: 0; max-width: 70ch; }
    .top { display: flex; gap: var(--uui-size-space-5); align-items: center; flex-wrap: wrap; margin-bottom: var(--uui-size-space-5); }
    .live { color: var(--uui-color-text-alt); }
    .grid { display: grid; grid-template-columns: minmax(0, 3fr) minmax(0, 2fr); gap: var(--uui-size-layout-1); align-items: start; }
    @media (max-width: 1100px) { .grid { grid-template-columns: 1fr; } }
    .editor { display: flex; flex-direction: column; gap: var(--uui-size-layout-1); }
    .preview { position: sticky; top: var(--uui-size-layout-1); }
    pre { white-space: pre-wrap; word-break: break-word; font-size: 13px; margin: 0; max-height: 75vh; overflow: auto;
      background: var(--uui-color-surface-alt); padding: var(--uui-size-space-4); border-radius: var(--uui-border-radius); }
    uui-input, uui-textarea, uui-select { width: 100%; }
    .help { color: var(--uui-color-text-alt); font-size: 0.9em; margin: 0 0 var(--uui-size-space-3); }
    .blocks { display: flex; flex-direction: column; gap: var(--uui-size-space-4); }
    .block { border: 1px solid var(--uui-color-border); border-radius: var(--uui-border-radius); background: var(--uui-color-surface); }
    .block-head { display: flex; align-items: center; gap: var(--uui-size-space-3); padding: var(--uui-size-space-3) var(--uui-size-space-4);
      border-bottom: 1px solid var(--uui-color-border); }
    .block-head .help { margin: 0; flex: 1; }
    .handle { cursor: grab; }
    .block-body { display: flex; flex-direction: column; gap: var(--uui-size-space-4); padding: var(--uui-size-space-4); }
    label { display: flex; flex-direction: column; gap: var(--uui-size-space-2); font-weight: 600; }
    .row { display: flex; gap: var(--uui-size-space-4); }
    .row label { flex: 1; }
    details summary { cursor: pointer; margin-bottom: var(--uui-size-space-3); }
    details label { margin-bottom: var(--uui-size-space-4); }
    .add { display: flex; flex-wrap: wrap; gap: var(--uui-size-space-3); margin-top: var(--uui-size-space-5); }
    .errors { color: var(--uui-color-danger); }
    .actions { display: flex; justify-content: flex-end; align-items: center; gap: var(--uui-size-space-4); }
    .ok { color: var(--uui-color-positive); }
  `;
}

customElements.define('lait-llms-dashboard', LaitLlmsDashboard);
export default LaitLlmsDashboard;

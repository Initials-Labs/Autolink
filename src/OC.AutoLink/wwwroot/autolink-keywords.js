import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, css, nothing, repeat, when } from '@umbraco-cms/backoffice/external/lit';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import '@umbraco-cms/backoffice/external/uui';

const API = '/umbraco/management/api/v1/autolink';
const EVERYWHERE = '00000000-0000-0000-0000-000000000000';

/** Skip codes from the server to localisation keys. The wording lives in lang/en-gb.js. */
const REASON_KEYS = {
	self: 'ocAutoLink_reasonSelf',
	'hand-linked': 'ocAutoLink_reasonHandLinked',
	'skipped-element': 'ocAutoLink_reasonSkippedElement',
	limit: 'ocAutoLink_reasonLimit',
	contested: 'ocAutoLink_reasonContested',
};

/**
 * One row per keyword, detail on demand.
 *
 * Everything here was already on screen before and it was unreadable: every keyword fully expanded, and the
 * destination printed twice because "links to" and "tagged on" are the same page whenever nothing is contested. So
 * the row carries the summary, aligned in columns so it can be scanned down, and the detail opens underneath. Rows
 * needing a decision sort first and open themselves, because they are the only ones anybody has to act on.
 */
export default class OcAutoLinkKeywordsElement extends UmbLitElement {
	static properties = {
		_overview: { state: true },
		_report: { state: true },
		_culture: { state: true },
		_expanded: { state: true },
		_loading: { state: true },
		_error: { state: true },
		_busy: { state: true },
		_adding: { state: true },
		_newKeyword: { state: true },
		_newUrl: { state: true },
		_newLabel: { state: true },
		_newNofollow: { state: true },
	};

	#notifications;
	#auth;

	constructor() {
		super();
		this._overview = null;
		this._report = null;
		this._culture = null;
		this._expanded = new Set();
		this._loading = true;
		this._error = null;
		this._busy = null;
		this._adding = false;
		this._newKeyword = '';
		this._newUrl = '';
		this._newLabel = '';
		this._newNofollow = true;

		this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
			this.#notifications = context;
		});

		this.consumeContext(UMB_AUTH_CONTEXT, (context) => {
			this.#auth = context;
		});
	}

	connectedCallback() {
		super.connectedCallback();
		this.#load();
	}

	/**
	 * Plain fetch rather than umbHttpClient: that client routes failures through backoffice error handling, where a
	 * 401 from a package endpoint is indistinguishable from an expired session and signs the user out.
	 */
	async #request(method, path, body) {
		const token = await this.#auth?.getLatestToken();

		const response = await fetch(`${API}${path}`, {
			method,
			headers: {
				Authorization: `Bearer ${token ?? ''}`,
				...(body ? { 'Content-Type': 'application/json' } : {}),
			},
			body: body ? JSON.stringify(body) : undefined,
		});

		if (response.ok) {
			const text = await response.text();
			return { data: text ? JSON.parse(text) : null, error: null };
		}

		if (response.status === 401 || response.status === 403) {
			return {
				data: null,
				error: this.localize.term('ocAutoLink_notAuthorised', response.status),
			};
		}

		return { data: null, error: this.localize.term('ocAutoLink_requestFailed', response.status) };
	}

	async #load() {
		this._loading = true;
		this._error = null;

		// The keyword list and the scan answer the two halves. Both are cheap, so the screen always has both rather
		// than making the mentions something you go and ask for.
		const [keywords, scan] = await Promise.all([
			this.#request('GET', '/keywords'),
			this.#request('GET', '/scan'),
		]);

		if (keywords.error || scan.error) {
			this._error = keywords.error ?? scan.error;
			this._loading = false;
			return;
		}

		this._overview = keywords.data;
		this._report = scan.data;

		const cultures = keywords.data.cultures ?? [];
		if (!cultures.some((c) => c.culture === this._culture)) {
			const interesting = cultures.find((c) => c.conflicts > 0) ?? cultures.find((c) => c.total > 0) ?? cultures[0];
			this._culture = interesting?.culture ?? '';
		}

		// Anything needing a decision opens itself; there is nothing to think about on the rest.
		const expanded = new Set(this._expanded);
		for (const row of this.#rows()) {
			if (row.hasConflict) expanded.add(row.keyword);
		}

		this._expanded = expanded;
		this._loading = false;
	}

	#selected() {
		return (this._overview?.cultures ?? []).find((c) => c.culture === this._culture) ?? null;
	}

	#languageLabel(culture) {
		return culture.length === 0 ? this.localize.term('ocAutoLink_allLanguages') : culture;
	}

	/** Keyword rows for the language being viewed, the ones needing attention first. */
	#rows() {
		const rows = [...(this.#selected()?.keywords ?? [])];
		const mentions = this.#mentions();

		return rows.sort((a, b) => {
			if (a.hasConflict !== b.hasConflict) return a.hasConflict ? -1 : 1;

			const aCount = (mentions.get(a.keyword.toLowerCase()) ?? []).length;
			const bCount = (mentions.get(b.keyword.toLowerCase()) ?? []).length;
			if (aCount !== bCount) return bCount - aCount;

			return a.keyword.localeCompare(b.keyword);
		});
	}

	/** Keyword to the pages whose copy contains it, in the language being viewed. */
	#mentions() {
		const mentions = new Map();

		for (const page of this._report?.pages ?? []) {
			if (page.culture !== this._culture) continue;

			for (const placement of page.placements) {
				const key = placement.keyword.toLowerCase();
				mentions.set(key, [...(mentions.get(key) ?? []), { page, placement }]);
			}
		}

		return mentions;
	}

	/**
	 * Mentions grouped by page. A page can mention a keyword several times and get a different answer each time —
	 * linked once, then capped, then one sitting in a heading — which as sibling rows read like three unrelated
	 * findings about the same page.
	 */
	#byPage(mentions) {
		const groups = new Map();

		for (const { page, placement } of mentions) {
			const group = groups.get(page.pageKey) ?? { page, placements: [] };
			group.placements.push(placement);
			groups.set(page.pageKey, group);
		}

		return [...groups.values()];
	}

	/** What the page line should say: linked if anything linked, else switched off, else the first reason. */
	#primary(placements) {
		return (
			placements.find((p) => this.#state(p) === 'linked') ??
			placements.find((p) => this.#state(p) === 'off') ??
			placements[0]
		);
	}

	/** A DOM-safe id for a keyword, so aria-controls can reference its detail panel. */
	#panelId(keyword) {
		return `autolink-detail-${keyword.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`;
	}

	/** The localised clause explaining why a mention was not linked. */
	#reason(placement) {
		const key = REASON_KEYS[placement.skipReason];

		return key ? this.localize.term(key) : placement.skipReason;
	}

	#state(placement) {
		if (placement.suppressed) return 'off';
		return placement.skipReason ? 'skipped' : 'linked';
	}

	#toggle(keyword) {
		const expanded = new Set(this._expanded);
		expanded.has(keyword) ? expanded.delete(keyword) : expanded.add(keyword);
		this._expanded = expanded;
	}

	async #act(busyKey, method, path, body, message) {
		this._busy = busyKey;
		const { error } = await this.#request(method, path, body);
		this._busy = null;

		if (error) {
			this.#notify('danger', error);
			return;
		}

		this.#notify('positive', message);
		await this.#load();
	}

	#use(keyword, targetKey, targetName) {
		return this.#act(
			`use|${keyword}|${targetKey}`,
			'PUT',
			'/mapping',
			{ keyword, targetKey, culture: this._culture ?? '' },
			this.localize.term('ocAutoLink_nowLinksTo', keyword, targetName),
		);
	}

	async #addExternal() {
		const keyword = this._newKeyword.trim();
		const url = this._newUrl.trim();

		if (!keyword || !url) {
			this.#notify('danger', this.localize.term('ocAutoLink_addNeedsBoth'));
			return;
		}

		await this.#act(
			`add|${keyword}`,
			'PUT',
			'/mapping',
			{
				keyword,
				externalUrl: url,
				label: this._newLabel.trim() || null,
				nofollow: this._newNofollow,
				culture: this._culture ?? '',
			},
			this.localize.term('ocAutoLink_nowLinksTo', keyword, url),
		);

		this._adding = false;
		this._newKeyword = '';
		this._newUrl = '';
		this._newLabel = '';
	}

	#clearChoice(keyword, mappingCulture) {
		return this.#act(
			`clear|${keyword}`,
			'DELETE',
			`/mapping?keyword=${encodeURIComponent(keyword)}&culture=${encodeURIComponent(mappingCulture ?? '')}`,
			null,
			this.localize.term('ocAutoLink_backToAutomatic', keyword),
		);
	}

	#unlink(keyword, pageKey, name) {
		return this.#act(
			`off|${keyword}|${pageKey}`,
			'PUT',
			'/suppression',
			{ keyword, pageKey, culture: this._culture ?? '' },
			pageKey === EVERYWHERE
				? this.localize.term('ocAutoLink_willNotLinkAnywhere', keyword)
				: this.localize.term('ocAutoLink_willNotLinkOn', keyword, name),
		);
	}

	#allow(keyword, placement) {
		return this.#act(
			`on|${keyword}|${placement.suppressedPageKey}`,
			'DELETE',
			`/suppression?keyword=${encodeURIComponent(keyword)}&pageKey=${placement.suppressedPageKey}` +
				`&culture=${encodeURIComponent(placement.suppressedCulture ?? '')}`,
			null,
			this.localize.term('ocAutoLink_canLinkAgain', keyword),
		);
	}

	#notify(colour, message) {
		try {
			this.#notifications?.peek(colour, { data: { message } });
		} catch {
			// A missing notification context is not worth failing an action over.
		}
	}

	render() {
		if (this._loading && !this._overview) {
			return html`<uui-box><uui-loader></uui-loader></uui-box>`;
		}

		if (this._error) {
			return html`<uui-box headline=${this.localize.term('ocAutoLink_heading')}>
				<p>${this._error}</p>
				<uui-button
					look="secondary"
					label=${this.localize.term('ocAutoLink_tryAgain')}
					@click=${() => this.#load()}></uui-button>
			</uui-box>`;
		}

		return html`${this.#renderHeader()}${this.#renderList()}`;
	}

	#renderHeader() {
		const cultures = this._overview?.cultures ?? [];
		const selected = this.#selected();
		const mentions = this.#mentions();
		const rows = this.#rows();

		const linkedPages = new Set();
		for (const list of mentions.values()) {
			for (const { page, placement } of list) {
				if (this.#state(placement) === 'linked') linkedPages.add(page.pageKey);
			}
		}

		return html`
			<uui-box headline=${this.localize.term('ocAutoLink_heading')}>
				<div slot="header-actions">
					<uui-button
						look="secondary"
						label=${this._adding ? this.localize.term('ocAutoLink_cancel') : this.localize.term('ocAutoLink_addExternal')}
						@click=${() => {
							this._adding = !this._adding;
						}}></uui-button>
					<uui-button
						look="secondary"
						label=${this.localize.term('ocAutoLink_refresh')}
						@click=${() => this.#load()}></uui-button>
				</div>

				${when(
					cultures.length > 1,
					() => html`<div class="languages" role="group" aria-label=${this.localize.term('ocAutoLink_languageGroup')}>
						${repeat(
							cultures,
							(entry) => entry.culture,
							(entry) => html`<uui-button
								look=${entry.culture === this._culture ? 'primary' : 'outline'}
								color=${entry.conflicts > 0 ? 'danger' : 'default'}
								aria-pressed=${entry.culture === this._culture ? 'true' : 'false'}
								label="${this.#languageLabel(entry.culture)} (${entry.total})"
								@click=${() => {
									this._culture = entry.culture;
								}}></uui-button>`,
						)}
					</div>`,
				)}

				${when(this._adding, () => this.#renderAddForm())}

				<p class="totals">
					${this.localize.term('ocAutoLink_keywordCount', rows.length)}
					${when(
						selected?.conflicts,
						() => html`&middot;
							<span class="bad">${selected.conflicts} ${this.localize.term('ocAutoLink_needingDecision')}</span>`,
					)}
					&middot;
					${this.localize.term('ocAutoLink_linkingOnPages', linkedPages.size)}
					&middot;
					<span class="muted">${this.localize.term('ocAutoLink_pagesChecked', this._report?.pagesScanned ?? 0)}</span>
				</p>
			</uui-box>
		`;
	}

	#renderAddForm() {
		const busy = this._busy?.startsWith('add|');

		return html`
			<div class="add">
				<div class="add-head">
					${this.localize.term('ocAutoLink_addHeading', this.#languageLabel(this._culture ?? ''))}
				</div>

				<div class="add-fields">
					<uui-input
						label=${this.localize.term('ocAutoLink_fieldKeyword')}
						placeholder=${this.localize.term('ocAutoLink_fieldKeywordHint')}
						.value=${this._newKeyword}
						@input=${(event) => {
							this._newKeyword = event.target.value ?? '';
						}}></uui-input>

					<uui-input
						label=${this.localize.term('ocAutoLink_fieldUrl')}
						placeholder=${this.localize.term('ocAutoLink_fieldUrlHint')}
						.value=${this._newUrl}
						@input=${(event) => {
							this._newUrl = event.target.value ?? '';
						}}></uui-input>

					<uui-input
						label=${this.localize.term('ocAutoLink_fieldTitle')}
						placeholder=${this.localize.term('ocAutoLink_fieldTitleHint')}
						.value=${this._newLabel}
						@input=${(event) => {
							this._newLabel = event.target.value ?? '';
						}}></uui-input>
				</div>

				<label class="add-follow">
					<input
						type="checkbox"
						.checked=${this._newNofollow}
						@change=${(event) => {
							this._newNofollow = event.target.checked;
						}} />
					${this.localize.term('ocAutoLink_nofollowLabel')}
				</label>

				<div>
					<uui-button
						look="primary"
						color="positive"
						label=${this.localize.term('ocAutoLink_addLink')}
						?disabled=${busy}
						@click=${() => this.#addExternal()}></uui-button>
				</div>
			</div>
		`;
	}

	#renderList() {
		const rows = this.#rows();

		if (rows.length === 0) {
			return html`<uui-box>
				<p>${this.localize.term('ocAutoLink_noKeywords', this.#languageLabel(this._culture ?? ''))}</p>
			</uui-box>`;
		}

		const mentions = this.#mentions();

		return html`
			<uui-box>
				<div
					class="table"
					role="list"
					aria-label=${this.localize.term('ocAutoLink_heading')}
					aria-busy=${this._loading ? 'true' : 'false'}>
					<div class="head" aria-hidden="true">
						<span></span>
						<span>${this.localize.term('ocAutoLink_columnKeyword')}</span>
						<span>${this.localize.term('ocAutoLink_columnLinksTo')}</span>
						<span>${this.localize.term('ocAutoLink_columnMentions')}</span>
					</div>
					${repeat(
						rows,
						(row) => row.keyword,
						(row) => this.#renderRow(row, mentions.get(row.keyword.toLowerCase()) ?? []),
					)}
				</div>
			</uui-box>
		`;
	}

	#renderRow(row, mentions) {
		const open = this._expanded.has(row.keyword);
		// Counted per page, not per mention, so these agree with the list that opens underneath.
		const pages = this.#byPage(mentions).map((group) => this.#state(this.#primary(group.placements)));
		const counts = {
			linked: pages.filter((state) => state === 'linked').length,
			off: pages.filter((state) => state === 'off').length,
			skipped: pages.filter((state) => state === 'skipped').length,
		};

		return html`
			<div class="row ${row.hasConflict ? 'attention' : ''} ${open ? 'open' : ''}" role="listitem">
				<button
					class="caret"
					aria-expanded=${open ? 'true' : 'false'}
					aria-controls=${this.#panelId(row.keyword)}
					aria-label=${open
						? this.localize.term('ocAutoLink_hideDetail', row.keyword)
						: this.localize.term('ocAutoLink_showDetail', row.keyword)}
					@click=${() => this.#toggle(row.keyword)}>
					<span aria-hidden="true">${open ? '▾' : '▸'}</span>
				</button>

				<span class="keyword">${row.keyword}</span>

				<span class="destination">
					${row.hasConflict
						? html`<span class="bad">${this.localize.term('ocAutoLink_contestedSummary')}</span>`
						: row.source === 'external'
							? html`<a href=${row.url} target="_blank" rel="noopener noreferrer">${row.targetName}</a>
									<span class="path">${row.url}</span>
									<span class="pill">${this.localize.term('ocAutoLink_external')} <span aria-hidden="true">&#8599;</span></span>`
							: row.url
								? html`<a href=${row.url} target="_blank" rel="noopener">${row.targetName}</a>
										<span class="path">${row.url}</span>
										${when(
											row.source === 'manual',
											() => html`<span class="muted">${this.localize.term('ocAutoLink_chosenByHand')}</span>`,
										)}`
								: html`<span class="muted">${this.localize.term('ocAutoLink_nothingYet')}</span>`}
				</span>

				<span class="counts">
					${mentions.length === 0
						? html`<span class="muted">${this.localize.term('ocAutoLink_countNone')}</span>`
						: html`${when(
									counts.linked,
									() => html`<span class="good">${this.localize.term('ocAutoLink_countLinked', counts.linked)}</span>`,
								)}
								${when(
									counts.off,
									() => html`<span class="warn">${this.localize.term('ocAutoLink_countOff', counts.off)}</span>`,
								)}
								${when(
									counts.skipped,
									() => html`<span class="muted">${this.localize.term('ocAutoLink_countNotLinked', counts.skipped)}</span>`,
								)}`}
				</span>

				${when(open, () => this.#renderDetail(row, mentions))}
			</div>
		`;
	}

	#renderDetail(row, mentions) {
		return html`
			<div
				class="detail"
				id=${this.#panelId(row.keyword)}
				role="region"
				aria-label=${this.localize.term('ocAutoLink_detailFor', row.keyword)}>
				${this.#renderChoice(row)}
				${mentions.length === 0
					? html`<p class="muted">${this.localize.term('ocAutoLink_noMentions')}</p>`
					: html`<div class="caption">
								${this.localize.term('ocAutoLink_mentionedOn', this.#byPage(mentions).length)}
							</div>
							<div class="mentions">
								${repeat(
									this.#byPage(mentions),
									(group) => group.page.pageKey,
									(group) => this.#renderPageGroup(row, group),
								)}
							</div>
							${when(
								mentions.some((m) => this.#state(m.placement) === 'linked'),
								() => html`<uui-button
									look="secondary"
									color="danger"
									label=${this.localize.term('ocAutoLink_neverLink')}
									?disabled=${this._busy === `off|${row.keyword}|${EVERYWHERE}`}
									@click=${() => this.#unlink(row.keyword, EVERYWHERE, 'any page')}></uui-button>`,
							)}`}
			</div>
		`;
	}

	/** The destination half: only worth its own block when there is a decision to make or undo. */
	#renderChoice(row) {
		const busy = this._busy?.startsWith(`use|${row.keyword}`) || this._busy === `clear|${row.keyword}`;

		if (row.hasConflict) {
			return html`
				<div class="choice">
					<div class="choice-head">${this.localize.term('ocAutoLink_chooseHeading')}</div>
					${repeat(
						row.candidates,
						(candidate) => candidate.targetKey,
						(candidate) => html`<div class="option">
							<uui-button
								look="secondary"
								label=${this.localize.term('ocAutoLink_useThis')}
								?disabled=${busy}
								@click=${() => this.#use(row.keyword, candidate.targetKey, candidate.targetName)}></uui-button>
							<a href=${candidate.url} target="_blank" rel="noopener">${candidate.targetName}</a>
							<span class="path">${candidate.url}</span>
						</div>`,
					)}
					<p class="muted">${this.localize.term('ocAutoLink_untagInstead')}</p>
				</div>
			`;
		}

		if (row.source === 'external') {
			return html`<p class="chosen">
				${this.localize.term('ocAutoLink_externalFor', row.mappingCulture)}
				<uui-button
					look="secondary"
					color="danger"
					label=${this.localize.term('ocAutoLink_removeLink')}
					?disabled=${busy}
					@click=${() => this.#clearChoice(row.keyword, row.mappingCulture)}></uui-button>
			</p>`;
		}

		if (row.source === 'manual') {
			const tagged = row.candidates.length > 0;

			return html`<p class="chosen">
				${tagged
					? this.localize.term('ocAutoLink_chosenFor', row.mappingCulture)
					: this.localize.term('ocAutoLink_chosenNoTag', row.mappingCulture)}
				<uui-button
					look="secondary"
					label=${this.localize.term('ocAutoLink_undoChoice')}
					?disabled=${busy}
					@click=${() => this.#clearChoice(row.keyword, row.mappingCulture)}></uui-button>
			</p>`;
		}

		return nothing;
	}

	#renderPageGroup(row, { page, placements }) {
		const primary = this.#primary(placements);
		const extras = placements.filter((placement) => placement !== primary);

		return html`
			<div class="group">
				${this.#renderPageLine(row, page, primary)}
				${repeat(
					extras,
					(placement, index) => `${placement.skipReason}-${index}`,
					(placement) => html`<div class="note">
						${this.localize.term('ocAutoLink_anotherMention', this.#reason(placement))}
					</div>`,
				)}
			</div>
		`;
	}

	#renderPageLine(row, page, placement) {
		const state = this.#state(placement);
		const busy =
			this._busy === `off|${row.keyword}|${page.pageKey}` ||
			this._busy === `on|${row.keyword}|${placement.suppressedPageKey}`;

		const offEverywhere = placement.suppressedPageKey === EVERYWHERE;
		const offAllLanguages = placement.suppressedCulture === '';

		return html`
			<div class="mention ${state}">
				<a href=${page.url} target="_blank" rel="noopener">${page.name}</a>
				<span class="path">${page.url}</span>

				${state === 'linked'
					? html`<span class="good">${this.localize.term('ocAutoLink_linked')}</span>
							<uui-button
								look="secondary"
								label=${this.localize.term('ocAutoLink_doNotLinkHere')}
								?disabled=${busy}
								@click=${() => this.#unlink(row.keyword, page.pageKey, page.name)}></uui-button>`
					: state === 'off'
						? html`<span class="warn">
									${this.localize.term('ocAutoLink_switchedOff', offEverywhere, offAllLanguages)}
								</span>
								<uui-button
									look="secondary"
									label=${offEverywhere
										? this.localize.term('ocAutoLink_allowEverywhere')
										: this.localize.term('ocAutoLink_allowHere')}
									?disabled=${busy}
									@click=${() => this.#allow(row.keyword, placement)}></uui-button>`
						: html`<span class="muted">${this.localize.term('ocAutoLink_notLinked', this.#reason(placement))}</span>`}
			</div>
		`;
	}

	static styles = css`
		:host {
			display: flex;
			flex-direction: column;
			gap: var(--uui-size-layout-1);
			padding: var(--uui-size-layout-1);
		}

		.languages {
			display: flex;
			flex-wrap: wrap;
			gap: var(--uui-size-space-2);
			margin-bottom: var(--uui-size-space-4);
		}

		.totals {
			margin: 0;
		}

		.table {
			display: flex;
			flex-direction: column;
		}

		/* Head and rows share one explicit template, which is what lines the columns up. Deliberately not subgrid:
		   fixed tracks align in every browser and this needs no cleverness. */
		.head,
		.row {
			display: grid;
			grid-template-columns: 2rem 12rem minmax(14rem, 1fr) 13rem;
			column-gap: var(--uui-size-space-4);
			align-items: baseline;
			padding: var(--uui-size-space-3) 0;
			border-top: 1px solid var(--uui-color-divider);
		}

		.head {
			border-top: none;
			color: var(--uui-color-text-alt);
			font-size: var(--uui-type-small-size);
			text-transform: uppercase;
			letter-spacing: 0.04em;
		}

		.row.attention {
			box-shadow: inset 3px 0 0 0 var(--uui-color-danger);
		}

		/* An open keyword is a block, not just a tinted line: a wash to group it, a heavier rule to close it off from
		   the next keyword, and room at the bottom so the detail does not run into it. */
		.row.open {
			background: color-mix(in srgb, var(--uui-color-interactive, #3544b1) 4%, transparent);
			border-bottom: 2px solid var(--uui-color-divider);
			padding-bottom: var(--uui-size-space-4);
		}

		.row.open .keyword {
			font-size: var(--uui-type-h5-size, 1.1rem);
		}

		.caret {
			background: none;
			border: none;
			cursor: pointer;
			padding: 0;
			color: inherit;
			border-radius: 3px;
		}

		.caret:focus-visible {
			outline: 2px solid var(--uui-color-focus, var(--uui-color-interactive, #3544b1));
			outline-offset: 2px;
		}

		.keyword {
			font-weight: bold;
		}

		.destination,
		.counts {
			display: flex;
			flex-wrap: wrap;
			gap: var(--uui-size-space-2);
			align-items: baseline;
		}

		/* Detail spans every column, and hangs off a vertical rule under the keyword so it plainly belongs to the row
		   above rather than floating between two of them. */
		.detail {
			grid-column: 1 / -1;
			display: flex;
			flex-direction: column;
			gap: var(--uui-size-space-3);
			margin: var(--uui-size-space-4) 0 0 0.6rem;
			padding: 0 0 0 var(--uui-size-space-5);
			border-left: 2px solid color-mix(in srgb, var(--uui-color-interactive, #3544b1) 25%, transparent);
		}

		.caption {
			color: var(--uui-color-text-alt);
			font-size: var(--uui-type-small-size);
			text-transform: uppercase;
			letter-spacing: 0.04em;
		}

		.choice {
			display: flex;
			flex-direction: column;
			gap: var(--uui-size-space-2);
			padding: var(--uui-size-space-3);
			border-left: 3px solid var(--uui-color-danger);
			background: var(--uui-color-surface);
		}

		.choice-head {
			font-weight: bold;
		}

		/* Fixed tracks, not max-content: every mention row must use the same columns or nothing lines up down the
		   list, which is what made these hard to follow. Same reasoning as the keyword table above. */
		.group {
			border-top: 1px solid var(--uui-color-divider);
			padding: var(--uui-size-space-2) 0;
		}

		.group:hover {
			background: color-mix(in srgb, var(--uui-color-interactive, #3544b1) 10%, transparent);
		}

		.mention {
			display: grid;
			grid-template-columns: minmax(7rem, 14rem) minmax(6rem, 1fr) 12rem 12rem;
			column-gap: var(--uui-size-space-3);
			align-items: baseline;
		}

		/* Indented under the page it belongs to, and quiet: these are footnotes about one page, not findings. */
		.note {
			padding-left: var(--uui-size-space-4);
			color: var(--uui-color-text-alt);
			font-size: var(--uui-type-small-size);
			font-style: italic;
		}

		.option {
			display: grid;
			grid-template-columns: 7rem minmax(7rem, 14rem) minmax(6rem, 1fr);
			column-gap: var(--uui-size-space-3);
			align-items: baseline;
			padding: var(--uui-size-space-2) 0;
			border-top: 1px solid var(--uui-color-divider);
		}

		.mentions {
			display: flex;
			flex-direction: column;
		}

		/* A heavier rule under the keyword row, so the detail reads as belonging to it rather than floating. */
		.detail .mentions {
			border-top: 1px solid var(--uui-color-divider);
		}

		.mentions .group:first-child,
		.choice .option:first-child {
			border-top: none;
		}

		.mention.off a,
		.mention.skipped a {
			text-decoration: line-through;
		}

		.add {
			display: flex;
			flex-direction: column;
			gap: var(--uui-size-space-3);
			margin-bottom: var(--uui-size-space-5);
			padding: var(--uui-size-space-4);
			border-left: 3px solid var(--uui-color-positive);
			background: var(--uui-color-surface-alt, rgba(0, 0, 0, 0.03));
		}

		.add-head {
			font-weight: bold;
		}

		.add-fields {
			display: grid;
			grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
			gap: var(--uui-size-space-3);
		}

		.add-follow {
			display: flex;
			align-items: center;
			gap: var(--uui-size-space-2);
			color: var(--uui-color-text-alt);
			font-size: var(--uui-type-small-size);
		}

		.chosen {
			margin: 0;
			display: flex;
			gap: var(--uui-size-space-3);
			align-items: center;
			flex-wrap: wrap;
		}

		.path {
			color: var(--uui-color-text-alt);
			font-family: monospace;
			font-size: var(--uui-type-small-size);
		}

		.muted {
			color: var(--uui-color-text-alt);
			font-size: var(--uui-type-small-size);
		}

		/* A pill rather than more grey text: leaving the site is a different kind of destination, not a note about
		   provenance, so it earns the one badge on the row. The arrow says outbound without needing an icon set. */
		.pill {
			display: inline-flex;
			align-items: center;
			gap: 0.3em;
			padding: 0.05rem 0.55rem;
			border-radius: 999px;
			font-size: var(--uui-type-small-size);
			font-weight: 700;
			letter-spacing: 0.03em;
			text-transform: uppercase;
			white-space: nowrap;
			color: var(--uui-color-interactive, #3544b1);
			background: color-mix(in srgb, var(--uui-color-interactive, #3544b1) 12%, transparent);
			border: 1px solid color-mix(in srgb, var(--uui-color-interactive, #3544b1) 30%, transparent);
		}

		.good {
			color: var(--uui-color-positive);
			font-size: var(--uui-type-small-size);
		}

		.warn {
			color: var(--uui-color-warning-standalone, var(--uui-color-warning));
			font-size: var(--uui-type-small-size);
		}

		.bad {
			color: var(--uui-color-danger);
		}
	`;
}

customElements.define('oc-autolink-keywords', OcAutoLinkKeywordsElement);

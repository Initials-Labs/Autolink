import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, css, nothing, repeat, when } from '@umbraco-cms/backoffice/external/lit';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import '@umbraco-cms/backoffice/external/uui';

// Umbraco's own Multi URL Picker input, the one behind the Umbraco.MultiUrlPicker property editor. Importing it
// defines <umb-input-multi-url>, and with it the link picker modal that already knows how to offer a page or a
// typed URL. Rebuilding either of those here would mean a picker that behaves almost like the one editors know.
import '@umbraco-cms/backoffice/multi-url-picker';

const API = '/umbraco/management/api/v1/autolink';
const EVERYWHERE = '00000000-0000-0000-0000-000000000000';

/** Skip codes from the server to localisation keys. The wording lives in lang/en.js. */
const REASON_KEYS = {
	self: 'initialsAutoLink_reasonSelf',
	'hand-linked': 'initialsAutoLink_reasonHandLinked',
	'skipped-element': 'initialsAutoLink_reasonSkippedElement',
	limit: 'initialsAutoLink_reasonLimit',
};

/**
 * One row per keyword, detail on demand.
 *
 * This screen owns the keywords outright. There is no property on any document type to fill in and no tag to
 * apply: a keyword exists because somebody added it here, and it points wherever they pointed it. Which is why
 * the destination is Umbraco's Multi URL Picker rather than a URL box — a page and an outside URL are the same
 * decision, and the picker is where an editor already expects to make it.
 *
 * Rows are summaries, aligned in columns so they can be scanned down, with detail underneath. Rows needing
 * attention sort first and open themselves, because they are the only ones anybody has to act on.
 */
export default class InitialsAutoLinkKeywordsElement extends UmbLitElement {
	static properties = {
		_overview: { state: true },
		_report: { state: true },
		_culture: { state: true },
		_expanded: { state: true },
		_loading: { state: true },
		_error: { state: true },
		_busy: { state: true },
		_adding: { state: true },
		_editing: { state: true },
		_formKeyword: { state: true },
		_formLink: { state: true },
		_formLabel: { state: true },
		_formNofollow: { state: true },
		_formTargetNoticed: { state: true },
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
		this.#resetForm();

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
				error: this.localize.term('initialsAutoLink_notAuthorised', response.status),
			};
		}

		// A rejected save says why in the body. Worth showing: every validation rule on the way in is a sentence
		// somebody needs to read, and "the request failed (400)" is not that sentence.
		if (response.status === 400) {
			const reason = await response.text().catch(() => '');
			if (reason) {
				return { data: null, error: reason };
			}
		}

		return { data: null, error: this.localize.term('initialsAutoLink_requestFailed', response.status) };
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
			const interesting =
				cultures.find((c) => c.unresolved > 0) ?? cultures.find((c) => c.total > 0) ?? cultures[0];
			this._culture = interesting?.culture ?? '';
		}

		// Anything broken opens itself; there is nothing to think about on the rest.
		const expanded = new Set(this._expanded);
		for (const row of this.#rows()) {
			if (row.source === 'unresolved') expanded.add(row.keyword);
		}

		this._expanded = expanded;
		this._loading = false;
	}

	#selected() {
		return (this._overview?.cultures ?? []).find((c) => c.culture === this._culture) ?? null;
	}

	#languageLabel(culture) {
		return !culture || culture.length === 0
			? this.localize.term('initialsAutoLink_allLanguages')
			: this.#displayCulture(culture);
	}

	/**
	 * Backoffice edit URL for a document. The variant segment is load-bearing: a variant document opens on a
	 * culture, an invariant one only on the literal `invariant`, and the wrong one renders a blank workspace
	 * rather than an error. Left relative and without target=_blank on purpose, so a click stays inside the
	 * running backoffice — the router soft-navigates, which is also the only load that reliably renders.
	 */
	#editUrl(key, variesByCulture, culture) {
		const variant = variesByCulture ? this.#displayCulture(culture || this.#firstLanguage()) : 'invariant';
		return `/umbraco/section/content/workspace/document/edit/${key}/${variant}`;
	}

	/** First real language in the overview, for opening a variant document from the all-languages tab. */
	#firstLanguage() {
		return (this._overview?.cultures ?? []).map((entry) => entry.culture).find((entry) => entry) ?? '';
	}

	/**
	 * A culture code as the registry spells it.
	 *
	 * Stored rows carry a lower-cased culture, because it is an index key compared case-insensitively — so the row
	 * for en-GB comes back from the API as "en-gb". Showing that verbatim in a sentence reads as a bug. The
	 * registry's own list is the display spelling.
	 */
	#displayCulture(culture) {
		const known = (this._overview?.cultures ?? []).find(
			(entry) => entry.culture.toLowerCase() === culture.toLowerCase(),
		);

		return known?.culture ?? culture;
	}

	/** Keyword rows for the language being viewed, the ones needing attention first. */
	#rows() {
		const rows = [...(this.#selected()?.keywords ?? [])];
		const mentions = this.#mentions();

		return rows.sort((a, b) => {
			const aBroken = a.source === 'unresolved';
			const bBroken = b.source === 'unresolved';
			if (aBroken !== bBroken) return aBroken ? -1 : 1;

			const aCount = (mentions.get(a.keyword.toLowerCase()) ?? []).length;
			const bCount = (mentions.get(b.keyword.toLowerCase()) ?? []).length;
			if (aCount !== bCount) return bCount - aCount;

			return a.keyword.localeCompare(b.keyword);
		});
	}

	/** Keyword to the pages whose copy contains it, in the language being viewed — or in every language on the
	 * all-languages tab, whose keywords apply everywhere. Scan rows always carry a concrete culture, so an exact
	 * match against the all tab's empty culture would report every keyword as mentioned nowhere. */
	#mentions() {
		const mentions = new Map();
		const allLanguages = !this._culture;

		for (const page of this._report?.pages ?? []) {
			if (!allLanguages && page.culture !== this._culture) continue;

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

		// Keyed by page AND culture: on the all-languages tab the same page appears once per language, each with
		// its own URL, its own edit link and its own suppression state, so merging them would cross wires.
		for (const { page, placement } of mentions) {
			const key = `${page.pageKey}|${page.culture}`;
			const group = groups.get(key) ?? { page, placements: [] };
			group.placements.push(placement);
			groups.set(key, group);
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
			return false;
		}

		this.#notify('positive', message);
		await this.#load();
		return true;
	}

	// ---------------------------------------------------------------------------------------------------------
	// The form: one shape for adding a keyword and for changing where an existing one points.
	// ---------------------------------------------------------------------------------------------------------

	#resetForm() {
		this._adding = false;
		this._editing = null;
		this._formKeyword = '';
		this._formLink = null;
		this._formLabel = '';
		this._formNofollow = true;
		this._formTargetNoticed = false;
	}

	#formOpen() {
		return this._adding || this._editing !== null;
	}

	#openAdd() {
		this.#resetForm();
		this._adding = true;
	}

	#openEdit(row) {
		this.#resetForm();
		this._editing = row.keyword;
		this._formKeyword = row.keyword;
		this._formLabel = row.label ?? '';
		this._formNofollow = row.nofollow ?? true;
		this._formLink = row.externalUrl
			? { type: 'external', url: row.externalUrl, name: row.label ?? '' }
			: row.targetKey
				? { type: 'document', unique: row.targetKey, name: row.targetName ?? '', url: row.url ?? '' }
				: null;
	}

	/**
	 * What the picker handed back, if this package can store it.
	 *
	 * A media pick is turned away rather than quietly stored. Media resolves to a site-relative URL, and the
	 * validation on an external destination insists on an absolute http or https one on purpose — it is the only
	 * editor-typed string that reaches an href. Loosening it for a keyword pointing at a PDF is not a trade worth
	 * making here.
	 */
	#onLinkChange(event) {
		const link = event.target.urls?.[0] ?? null;

		if (link?.type === 'media') {
			event.target.urls = [];
			this._formLink = null;
			this._formTargetNoticed = false;
			this.#notify('warning', this.localize.term('initialsAutoLink_mediaNotSupported'));
			return;
		}

		this._formLink = link;

		// Nothing in an auto-link opens a new window, and the picker's checkbox cannot be hidden. Say so rather
		// than dropping it silently.
		this._formTargetNoticed = Boolean(link?.target);
	}

	#linkIsExternal(link) {
		return Boolean(link) && !link.unique;
	}

	async #saveKeyword() {
		const keyword = (this._editing ?? this._formKeyword).trim();
		const link = this._formLink;

		if (!keyword || !link) {
			this.#notify('danger', this.localize.term('initialsAutoLink_needsKeywordAndDestination'));
			return;
		}

		const body = { keyword, culture: this._culture ?? '' };
		let destination;

		if (this.#linkIsExternal(link)) {
			const url = (link.url ?? '').trim();
			const lower = url.toLowerCase();

			// Checked here as well as at the API, so a typo comes back as a sentence about the URL instead of a
			// rejected request.
			if (!lower.startsWith('http://') && !lower.startsWith('https://')) {
				this.#notify('danger', this.localize.term('initialsAutoLink_notAbsoluteUrl'));
				return;
			}

			body.externalUrl = url;
			body.label = this._formLabel.trim() || link.name || null;
			body.nofollow = this._formNofollow;
			destination = body.label || url;
		} else {
			body.targetKey = link.unique;
			destination = link.name || this.localize.term('initialsAutoLink_thePage');
		}

		const saved = await this.#act(
			`save|${keyword}`,
			'PUT',
			'/mapping',
			body,
			this.localize.term('initialsAutoLink_nowLinksTo', keyword, destination),
		);

		if (saved) {
			this.#resetForm();
		}
	}

	#removeKeyword(keyword, mappingCulture) {
		return this.#act(
			`remove|${keyword}`,
			'DELETE',
			`/mapping?keyword=${encodeURIComponent(keyword)}&culture=${encodeURIComponent(mappingCulture ?? '')}`,
			null,
			this.localize.term('initialsAutoLink_keywordRemoved', keyword),
		);
	}

	#unlink(keyword, pageKey, name) {
		return this.#act(
			`off|${keyword}|${pageKey}`,
			'PUT',
			'/suppression',
			{ keyword, pageKey, culture: this._culture ?? '' },
			pageKey === EVERYWHERE
				? this.localize.term('initialsAutoLink_willNotLinkAnywhere', keyword)
				: this.localize.term('initialsAutoLink_willNotLinkOn', keyword, name),
		);
	}

	#allow(keyword, placement) {
		return this.#act(
			`on|${keyword}|${placement.suppressedPageKey}`,
			'DELETE',
			`/suppression?keyword=${encodeURIComponent(keyword)}&pageKey=${placement.suppressedPageKey}` +
				`&culture=${encodeURIComponent(placement.suppressedCulture ?? '')}`,
			null,
			this.localize.term('initialsAutoLink_canLinkAgain', keyword),
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
			return html`<uui-box headline=${this.localize.term('initialsAutoLink_heading')}>
				<p>${this._error}</p>
				<uui-button
					look="secondary"
					label=${this.localize.term('initialsAutoLink_tryAgain')}
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
			<uui-box headline=${this.localize.term('initialsAutoLink_heading')}>
				<div slot="header-actions">
					<uui-button
						look="secondary"
						label=${this.#formOpen()
							? this.localize.term('initialsAutoLink_cancel')
							: this.localize.term('initialsAutoLink_addKeyword')}
						@click=${() => (this.#formOpen() ? this.#resetForm() : this.#openAdd())}></uui-button>
					<uui-button
						look="secondary"
						label=${this.localize.term('initialsAutoLink_refresh')}
						@click=${() => this.#load()}></uui-button>
				</div>

				${when(
					cultures.length > 1,
					() => html`<div class="languages" role="group" aria-label=${this.localize.term('initialsAutoLink_languageGroup')}>
						${repeat(
							cultures,
							(entry) => entry.culture,
							(entry) => html`<uui-button
								look=${entry.culture === this._culture ? 'primary' : 'outline'}
								color=${entry.unresolved > 0 ? 'danger' : 'default'}
								aria-pressed=${entry.culture === this._culture ? 'true' : 'false'}
								label="${this.#languageLabel(entry.culture)} (${entry.total})"
								@click=${() => {
									this._culture = entry.culture;
								}}></uui-button>`,
						)}
					</div>`,
				)}

				${when(this.#formOpen(), () => this.#renderForm())}

				<p class="totals">
					${this.localize.term('initialsAutoLink_keywordCount', rows.length)}
					${when(
						selected?.unresolved,
						() => html`&middot;
							<span class="bad">${this.localize.term('initialsAutoLink_needingAttention', selected.unresolved)}</span>`,
					)}
					&middot;
					${this.localize.term('initialsAutoLink_linkingOnPages', linkedPages.size)}
					&middot;
					<span class="muted">${this.localize.term('initialsAutoLink_pagesChecked', this._report?.pagesScanned ?? 0)}</span>
				</p>
			</uui-box>
		`;
	}

	#renderForm() {
		const editing = this._editing !== null;
		const busy = this._busy?.startsWith('save|');
		const language = this.#languageLabel(this._culture ?? '');
		const external = this.#linkIsExternal(this._formLink);

		return html`
			<div class="form">
				<div class="form-head">
					${editing
						? this.localize.term('initialsAutoLink_editHeading', this._editing, language)
						: this.localize.term('initialsAutoLink_addHeading', language)}
				</div>

				<div class="form-fields">
					<div class="field">
						<span class="field-label">${this.localize.term('initialsAutoLink_fieldKeyword')}</span>
						<uui-input
							label=${this.localize.term('initialsAutoLink_fieldKeyword')}
							placeholder=${this.localize.term('initialsAutoLink_fieldKeywordHint')}
							?disabled=${editing}
							.value=${this._formKeyword}
							@input=${(event) => {
								this._formKeyword = event.target.value ?? '';
							}}></uui-input>
					</div>

					<div class="field">
						<span class="field-label">${this.localize.term('initialsAutoLink_fieldDestination')}</span>
						<umb-input-multi-url
							max="1"
							hide-anchor
							.urls=${this._formLink ? [this._formLink] : []}
							@change=${(event) => this.#onLinkChange(event)}></umb-input-multi-url>
					</div>
				</div>

				${when(
					external,
					() => html`
						<div class="form-fields">
							<div class="field">
								<span class="field-label">${this.localize.term('initialsAutoLink_fieldTitle')}</span>
								<uui-input
									label=${this.localize.term('initialsAutoLink_fieldTitle')}
									placeholder=${this.localize.term('initialsAutoLink_fieldTitleHint')}
									.value=${this._formLabel}
									@input=${(event) => {
										this._formLabel = event.target.value ?? '';
									}}></uui-input>
							</div>
						</div>

						<label class="form-follow">
							<input
								type="checkbox"
								.checked=${this._formNofollow}
								@change=${(event) => {
									this._formNofollow = event.target.checked;
								}} />
							${this.localize.term('initialsAutoLink_nofollowLabel')}
						</label>
					`,
				)}

				${when(
					this._formTargetNoticed,
					() => html`<p class="muted">${this.localize.term('initialsAutoLink_targetNotUsed')}</p>`,
				)}

				<div>
					<uui-button
						look="primary"
						color="positive"
						label=${this.localize.term('initialsAutoLink_saveKeyword')}
						?disabled=${busy}
						@click=${() => this.#saveKeyword()}></uui-button>
				</div>
			</div>
		`;
	}

	#renderList() {
		const rows = this.#rows();

		if (rows.length === 0) {
			return html`<uui-box>
				<p>${this.localize.term('initialsAutoLink_noKeywords', this.#languageLabel(this._culture ?? ''))}</p>
				<uui-button
					look="primary"
					color="positive"
					label=${this.localize.term('initialsAutoLink_addKeyword')}
					@click=${() => this.#openAdd()}></uui-button>
			</uui-box>`;
		}

		const mentions = this.#mentions();

		return html`
			<uui-box>
				<div
					class="table"
					role="list"
					aria-label=${this.localize.term('initialsAutoLink_heading')}
					aria-busy=${this._loading ? 'true' : 'false'}>
					<div class="head" aria-hidden="true">
						<span></span>
						<span>${this.localize.term('initialsAutoLink_columnKeyword')}</span>
						<span>${this.localize.term('initialsAutoLink_columnLinksTo')}</span>
						<span>${this.localize.term('initialsAutoLink_columnMentions')}</span>
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
		const broken = row.source === 'unresolved';

		// Counted per page, not per mention, so these agree with the list that opens underneath.
		const pages = this.#byPage(mentions).map((group) => this.#state(this.#primary(group.placements)));
		const counts = {
			linked: pages.filter((state) => state === 'linked').length,
			off: pages.filter((state) => state === 'off').length,
			skipped: pages.filter((state) => state === 'skipped').length,
		};

		// Derived once and passed down: the button's aria-controls and the panel's id have to be the same string.
		const panelId = this.#panelId(row.keyword);

		return html`
			<div class="row ${broken ? 'attention' : ''} ${open ? 'open' : ''}" role="listitem">
				<button
					class="caret"
					aria-expanded=${open ? 'true' : 'false'}
					aria-controls=${panelId}
					aria-label=${open
						? this.localize.term('initialsAutoLink_hideDetail', row.keyword)
						: this.localize.term('initialsAutoLink_showDetail', row.keyword)}
					@click=${() => this.#toggle(row.keyword)}>
					<span aria-hidden="true">${open ? '▾' : '▸'}</span>
				</button>

				<span class="keyword">${row.keyword}</span>

				<span class="destination">
					${broken
						? html`<span class="bad">${this.localize.term('initialsAutoLink_unresolvedSummary')}</span>
								${when(row.externalUrl, () => html`<span class="path">${row.externalUrl}</span>`)}`
						: row.source === 'external'
							? html`<a href=${row.url} target="_blank" rel="noopener noreferrer">${row.targetName}</a>
									<span class="path">${row.url}</span>
									<span class="pill">${this.localize.term('initialsAutoLink_external')} <span aria-hidden="true">&#8599;</span></span>`
							: html`<a
										href=${this.#editUrl(row.targetKey, row.targetVariesByCulture, this._culture)}
										title=${this.localize.term('initialsAutoLink_editInBackoffice')}
										>${row.targetName}</a
									>
									<a class="path" href=${row.url} target="_blank" rel="noopener" title=${this.localize.term('initialsAutoLink_viewOnSite')}
										>${row.url}</a
									>`}
				</span>

				<span class="counts">
					${mentions.length === 0
						? html`<span class="muted">${this.localize.term('initialsAutoLink_countNone')}</span>`
						: html`${when(
									counts.linked,
									() => html`<span class="good">${this.localize.term('initialsAutoLink_countLinked', counts.linked)}</span>`,
								)}
								${when(
									counts.off,
									() => html`<span class="warn">${this.localize.term('initialsAutoLink_countOff', counts.off)}</span>`,
								)}
								${when(
									counts.skipped,
									() => html`<span class="muted">${this.localize.term('initialsAutoLink_countNotLinked', counts.skipped)}</span>`,
								)}`}
				</span>

				${when(open, () => this.#renderDetail(row, mentions, panelId))}
			</div>
		`;
	}

	#renderDetail(row, mentions, panelId) {
		return html`
			<div
				class="detail"
				id=${panelId}
				role="region"
				aria-label=${this.localize.term('initialsAutoLink_detailFor', row.keyword)}>
				${this.#renderDestination(row)}
				${mentions.length === 0
					? html`<p class="muted">${this.localize.term('initialsAutoLink_noMentions', !this._culture)}</p>`
					: html`<div class="caption">
								${this.localize.term('initialsAutoLink_mentionedOn', this.#byPage(mentions).length)}
							</div>
							<div class="mentions">
								${repeat(
									this.#byPage(mentions),
									(group) => `${group.page.pageKey}|${group.page.culture}`,
									(group) => this.#renderPageGroup(row, group),
								)}
							</div>
							${when(
								mentions.some((m) => this.#state(m.placement) === 'linked'),
								() => html`<div class="detail-actions">
									<uui-button
										look="outline"
										color="danger"
										label=${this.localize.term('initialsAutoLink_neverLink')}
										?disabled=${this._busy === `off|${row.keyword}|${EVERYWHERE}`}
										@click=${() => this.#unlink(row.keyword, EVERYWHERE, 'any page')}></uui-button>
								</div>`,
							)}`}
			</div>
		`;
	}

	/** Where this keyword goes, and the two things you can do about it. */
	#renderDestination(row) {
		const busy = this._busy === `remove|${row.keyword}` || this._busy?.startsWith(`save|${row.keyword}`);
		const broken = row.source === 'unresolved';

		return html`
			<div class="destination-block ${broken ? 'broken' : ''}">
				${when(
					broken,
					() => html`<p class="bad">
						${row.externalUrl
							? this.localize.term('initialsAutoLink_unresolvedExternalDetail')
							: this.localize.term('initialsAutoLink_unresolvedPageDetail')}
					</p>`,
				)}

				<p class="chosen">
					<span class="muted">
						${this.localize.term(
							'initialsAutoLink_setFor',
							this.#languageLabel(row.mappingCulture ?? ''),
							row.updatedBy ?? this.localize.term('initialsAutoLink_somebody'),
						)}
					</span>
					<uui-button
						look="outline"
						label=${this.localize.term('initialsAutoLink_changeDestination')}
						?disabled=${busy}
						@click=${() => this.#openEdit(row)}></uui-button>
					<uui-button
						look="outline"
						color="danger"
						label=${this.localize.term('initialsAutoLink_removeKeyword')}
						?disabled=${busy}
						@click=${() => this.#removeKeyword(row.keyword, row.mappingCulture)}></uui-button>
				</p>
			</div>
		`;
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
						${this.localize.term('initialsAutoLink_anotherMention', this.#reason(placement))}
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
				<a
					href=${this.#editUrl(page.pageKey, page.variesByCulture, page.culture)}
					title=${this.localize.term('initialsAutoLink_editInBackoffice')}
					>${page.name}</a
				>
				<!-- One grid cell, not two children: .mention maps children to columns by position. -->
				<span class="place">
					<a class="path" href=${page.url} target="_blank" rel="noopener" title=${this.localize.term('initialsAutoLink_viewOnSite')}
						>${page.url}</a
					>
					${when(
						!this._culture && page.culture,
						() => html`<span class="pill">${this.#displayCulture(page.culture)}</span>`,
					)}
				</span>

				${state === 'linked'
					? html`<span class="good">${this.localize.term('initialsAutoLink_linked')}</span>
							<uui-button
								look="outline"
								label=${this.localize.term('initialsAutoLink_doNotLinkHere')}
								?disabled=${busy}
								@click=${() => this.#unlink(row.keyword, page.pageKey, page.name)}></uui-button>`
					: state === 'off'
						? html`<span class="warn">
									${this.localize.term('initialsAutoLink_switchedOff', offEverywhere, offAllLanguages)}
								</span>
								<uui-button
									look="outline"
									label=${offEverywhere
										? this.localize.term('initialsAutoLink_allowEverywhere')
										: this.localize.term('initialsAutoLink_allowHere')}
									?disabled=${busy}
									@click=${() => this.#allow(row.keyword, placement)}></uui-button>`
						: html`<span class="muted">${this.localize.term('initialsAutoLink_notLinked', this.#reason(placement))}</span>`}
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
		   fixed tracks align in every browser and this needs no cleverness.
		   Proportional tracks rather than fixed ones, because a fixed destination column plus 1fr stranded the
		   mentions count on its own at the far right of a wide screen. */
		.head,
		.row {
			display: grid;
			grid-template-columns: 2rem minmax(9rem, 1fr) minmax(16rem, 2.4fr) minmax(9rem, 1fr);
			column-gap: var(--uui-size-space-4);
			align-items: baseline;
			padding: var(--uui-size-space-4) 0;
			border-top: 1px solid var(--uui-color-divider);
		}

		.head {
			border-top: none;
			color: var(--uui-color-text-alt);
			font-size: var(--uui-type-small-size);
			text-transform: uppercase;
			letter-spacing: 0.04em;
			padding-bottom: var(--uui-size-space-2);
		}

		/* The count column reads as a right-hand metric, the way a total does in any table. Left-aligned it just
		   floats in the middle of nowhere. */
		.head span:last-of-type,
		.counts {
			justify-content: flex-end;
			text-align: right;
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
			/* Centred, not baseline: the external pill has its own box and sits low against a text baseline. */
			align-items: center;
		}

		.destination a {
			font-weight: 600;
		}

		/* Detail spans every column, and hangs off a vertical rule under the keyword so it plainly belongs to the row
		   above rather than floating between two of them. */
		.detail {
			grid-column: 1 / -1;
			display: flex;
			flex-direction: column;
			gap: var(--uui-size-space-3);
			/* 2rem is the caret track, so the rule sits under the caret and the detail's text starts on the same
			   line as the keyword above it. It used to hang slightly left of the keyword, which read as a mistake. */
			margin: var(--uui-size-space-4) 0 0 2rem;
			padding: 0 0 0 var(--uui-size-space-4);
			border-left: 2px solid color-mix(in srgb, var(--uui-color-interactive, #3544b1) 25%, transparent);
		}

		.caption {
			color: var(--uui-color-text-alt);
			font-size: var(--uui-type-small-size);
			text-transform: uppercase;
			letter-spacing: 0.04em;
		}

		.destination-block.broken {
			padding: var(--uui-size-space-3);
			border-left: 3px solid var(--uui-color-danger);
			background: var(--uui-color-surface);
		}

		.destination-block p {
			margin: 0 0 var(--uui-size-space-2) 0;
		}

		.destination-block p:last-child {
			margin-bottom: 0;
		}

		/* Fixed tracks, not max-content: every mention row must use the same columns or nothing lines up down the
		   list, which is what made these hard to follow. Same reasoning as the keyword table above. */
		.group {
			border-top: 1px solid var(--uui-color-divider);
			/* Horizontal padding too: the hover wash paints the full row width, and text sitting flush against
			   its edge reads as a rendering fault rather than a design. */
			padding: var(--uui-size-space-2) var(--uui-size-space-4);
		}

		.group:hover {
			background: color-mix(in srgb, var(--uui-color-interactive, #3544b1) 10%, transparent);
		}

		/* Same reasoning as the keyword table: proportional tracks, and the action pinned right so the buttons form
		   one column down the list instead of drifting with the length of each URL. */
		.mention {
			display: grid;
			grid-template-columns: minmax(9rem, 1.5fr) minmax(8rem, 2fr) minmax(6rem, 1fr) 10rem;
			column-gap: var(--uui-size-space-3);
			align-items: center;
			min-height: var(--uui-size-10, 2.5rem);
		}

		.mention > :nth-child(4) {
			justify-self: end;
		}

		/* A skip reason is a sentence, not a status word, so it gets the status and action columns to wrap in. */
		.mention.skipped > .muted {
			grid-column: 3 / -1;
			text-align: right;
		}

		/* Indented under the page it belongs to, and quiet: these are footnotes about one page, not findings. */
		.note {
			padding-left: var(--uui-size-space-4);
			color: var(--uui-color-text-alt);
			font-size: var(--uui-type-small-size);
			font-style: italic;
		}

		.mentions {
			display: flex;
			flex-direction: column;
		}

		/* A heavier rule under the keyword row, so the detail reads as belonging to it rather than floating. */
		.detail .mentions {
			border-top: 1px solid var(--uui-color-divider);
		}

		.mentions .group:first-child {
			border-top: none;
		}

		.mention.off a,
		.mention.skipped a {
			text-decoration: line-through;
		}

		.form {
			display: flex;
			flex-direction: column;
			gap: var(--uui-size-space-3);
			margin-bottom: var(--uui-size-space-5);
			padding: var(--uui-size-space-4);
			border-left: 3px solid var(--uui-color-positive);
			background: var(--uui-color-surface-alt, rgba(0, 0, 0, 0.03));
		}

		.form-head {
			font-weight: bold;
		}

		.form-fields {
			display: grid;
			grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
			gap: var(--uui-size-space-4);
			align-items: start;
		}

		/* Every field is a label stacked over its control. The picker is a stack of its own — a ref-node once
		   something is chosen, a full-width placeholder button before that — and it only lines up with the keyword
		   box if that one carries a label of the same height rather than relying on its placeholder. */
		.field {
			display: flex;
			flex-direction: column;
			gap: var(--uui-size-space-1);
		}

		.field uui-input {
			width: 100%;
		}

		.field-label {
			color: var(--uui-color-text-alt);
			font-size: var(--uui-type-small-size);
			font-weight: 600;
		}

		/* Left, with the fields, rather than stretched across the form by the parent's flex stretch. */
		.detail-actions,
		.form > div:last-child {
			display: flex;
			justify-content: flex-start;
		}

		.form-follow {
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

		/* URL and language pill share the mention grid's one path cell, so the pill hugs its text instead of
		   being stretched across a grid track of its own. */
		.place {
			display: flex;
			align-items: center;
			gap: var(--uui-size-space-2);
			min-width: 0;
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

customElements.define('initials-autolink-keywords', InitialsAutoLinkKeywordsElement);

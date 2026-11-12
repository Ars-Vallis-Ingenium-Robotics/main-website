# Content & decisions to confirm

Facts on the site come from the club's own documents and from Robert (see
`src/AviSite.Web/Content/ClubFacts.cs`; each list notes its source). These are the things a
human still needs to confirm or supply.

## Confirmed (Nov 2026, Robert)
- [x] Founders: Robert Voss, Declan Doss, Timothy Cromwell
- [x] 102/100 Engineering Presentation record claim
- [x] Officer names can be public for the private demo
- [x] Contact email: contact@avirobotics.org
- [x] Member count: 51 (2026 tech doc title page)
- [x] Mentors: Dalton Cowan (2027 season), Adam Garabedian (renewed for 2027)
- [x] Treasurer spelling: Jaime Jaurigue (his own forms; the minutes say "Jaurique")

## Please confirm
- [ ] **SMS 2026 org chart** (/sms/company): reconstructed from the Sept 2025 structure doc,
      the presentation outline and the tech doc. CFO Jaurigue, CMO Maria Roceo Sy (spelling?),
      CPSO Flores under the COO; subsystem leads under the CTO/CDO.
- [ ] **Sam Heinrichs** vs "Heinrich"
- [ ] **Sponsor tiers**: the wall groups logos by their size on the Worlds shirt
      ("Headline", "Major", "Partners", "Supporters"). Real tier names/amounts? Is the circular
      logo "AC Electrical Construction"?
- [ ] **Discord invite**: discord.com/invite/xerHCd25XR (8/28/26 agenda)
- [ ] **Meetings**: Fridays 5:30–7:30 PM, EE 389
- [ ] **June 4 pool test guests**: the copy says the university president, the LCOE dean and
      Precision's owner attended, but photos are not captioned with their names. Add names?
- [ ] **History "More" text**: short expansions written from the docs and photo folders. Read once.

## Still needed
- [ ] **Faculty advisor headshots** (Dr. Bani Milhim, Dr. Wu): fresnostate.edu is blocked from
      the build sandbox. Save the two photos into `tools/images/` and add them to
      `people.manifest.json`.
- [ ] **Adam Garabedian**: current photo is a small LinkedIn screenshot; the original file would be sharper.
- [ ] **SMS videos**: the three Drive files must be shared "Anyone with the link can view", or the
      embeds show a sign-in wall. Alternative: upload to YouTube (unlisted) and swap the IDs.
- [ ] **AVI Club Tee**: no photo of anyone wearing it found yet. Hidden (`active: false`) until
      there's a photo + price.

## Shop & donations
- [ ] SMS Team Tee price/sizes for the next run
- [ ] PayPal: whose business account; sandbox credentials for a real test; refund policy text
- [ ] Donations: confirm PayPal account is the nonprofit's, and the receipt wording
      ("No goods or services were provided…") with the treasurer

## Brand / assets
- [x] Palette, Porcine (license confirmed), logos
- [x] Case rule: Porcine is always uppercase; buttons/nav are sentence case (tested)
- [ ] SVG logos: none exist; PNG masters go through the image pipeline

## Facts deliberately NOT published
- Mailing address on the IRS letters (looks like a home address)
- Personal emails/phone numbers from internal docs

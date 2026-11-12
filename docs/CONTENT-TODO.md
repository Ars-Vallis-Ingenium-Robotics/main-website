# Content & decisions to confirm

Facts on the site now come from the club's own documents (see `src/AviSite.Web/Content/ClubFacts.cs`,
each list notes its source). These are the things a human still needs to confirm.

## Please confirm
- [ ] **Founding story** (History page): "AVI's founders first competed in MATE with Engineering Renaissance's Crush Depth at Clovis Community College." Who founded AVI, and is "Sept 2025" the right founding month?
- [ ] **Record claim**: "102/100 Engineering Presentation, an all-time Pioneer-class record; 4th-highest overall Pioneer score" (from the Push-In Classroom Pitch doc).
- [ ] **Officer roster** (from 10/9/26 minutes): Declan Doss (President), Dottie Gunn (VP), Jaime Jaurique (Treasurer; the tech doc spells it "Jaurigue"), Ansh Josan (Secretary), Robert Voss (MATE CEO); leads Caleb Bahne, Arshiro Vang, Sebastien Dupere, Santiago Flores, Matthew Sedano. OK to publish names? Headshots?
- [ ] **SMS 2026 C-suite** on /sms/company: CEO Voss, COO Cromwell, CTO Doss, CDO Bahne, CPO/CSO Flores.
- [ ] **Contact email**: using avi.robotics.csufresno@gmail.com (from the club letterhead). Or an @avirobotics.org address?
- [ ] **Discord invite**: using discord.com/invite/xerHCd25XR (8/28/26 agenda). The new-member packet has a different one.
- [ ] **Meetings**: Fridays 5:30–7:30 PM, EE 389 (from the recruiting poster).
- [ ] **SMS for 2027**: Explorer class (from the new-member packet).
- [ ] **Micromouse**: wording on /teams and home ("forming, no name yet").

## Shop
- [ ] **SMS Team Tee**: uses the real Worlds shirt photos. Price, sizes, and which sponsors go on the back for this run?
- [ ] **AVI Club Tee**: is there an AVI-branded shirt? It's hidden (`active: false`) until there's a design + photo.
- [ ] PayPal: whose business account; sandbox credentials for a real test; refund policy text.

## Brand / assets
- [x] Palette, Porcine (license confirmed by Robert), logos
- [ ] SVG logos: none exist; PNG masters are used through the image pipeline
- [ ] More photos: run `tools/images/build_photos.py` with new entries (see README)

## Facts deliberately NOT published
- Mailing address on the IRS letters (looks like a home address)
- Personal emails/phone numbers from internal docs

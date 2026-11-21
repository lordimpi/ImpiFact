import { vi } from 'vitest';

// The first test of each file renders a page with several Optimus controls from cold, which takes 3-5 s when the
// whole project runs in parallel; the 5 s default then fails at random. 15 s still catches a real hang.
vi.setConfig({ testTimeout: 15_000 });

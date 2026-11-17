import { API_CLIENT_GENERATED } from './api-client';

describe('api placeholder', () => {
  it('reports that the client has not been generated yet', () => {
    expect(API_CLIENT_GENERATED).toBe(false);
  });
});

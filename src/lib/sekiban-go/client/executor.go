package client

import (
	"bytes"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"strings"
	"time"

	"github.com/J-Tech-Japan/sekiban-go/domain"
)

const (
	defaultTimeout = 30 * time.Second
	maxRetries     = 3
)

// SekibanRuntimeClient is an HTTP client for the WasmServer serialized API.
type SekibanRuntimeClient struct {
	baseURL    string
	httpClient *http.Client
	// TagProjectorMap maps tag group -> projector name.
	TagProjectorMap map[string]string
}

// NewSekibanRuntimeClient creates a new client.
func NewSekibanRuntimeClient(baseURL string, tagProjectorMap map[string]string) *SekibanRuntimeClient {
	return &SekibanRuntimeClient{
		baseURL:         strings.TrimRight(baseURL, "/"),
		httpClient:      &http.Client{Timeout: defaultTimeout},
		TagProjectorMap: tagProjectorMap,
	}
}

// TagStateResult from the serialized tag-state endpoint.
type TagStateResult struct {
	Tag                  string
	TagStateId           string
	PayloadJson          string
	Version              int
	LastSortableUniqueId string
}

// GetTagState fetches tag state from the WasmServer.
func (c *SekibanRuntimeClient) GetTagState(tagGroup, tagId string) (TagStateResult, error) {
	projector := c.TagProjectorMap[tagGroup]
	if projector == "" {
		return TagStateResult{}, fmt.Errorf("unknown projector for tag group '%s'", tagGroup)
	}
	tagStateId := fmt.Sprintf("%s:%s:%s", tagGroup, tagId, projector)

	var resp struct {
		Payload              *string `json:"payload"`
		Version              int     `json:"version"`
		LastSortedUniqueId   string  `json:"lastSortedUniqueId"`
		LastSortableUniqueId string  `json:"lastSortableUniqueId"`
	}

	if err := c.postJSON("/api/sekiban/serialized/tag-state", map[string]string{"tagStateId": tagStateId}, &resp); err != nil {
		return TagStateResult{}, err
	}

	payloadJson := "{}"
	if resp.Payload != nil && *resp.Payload != "" {
		decoded, err := base64.StdEncoding.DecodeString(*resp.Payload)
		if err == nil && len(decoded) > 0 {
			payloadJson = string(decoded)
		}
	}

	lastSortable := resp.LastSortableUniqueId
	if lastSortable == "" {
		lastSortable = resp.LastSortedUniqueId
	}

	return TagStateResult{
		Tag:                  fmt.Sprintf("%s:%s", tagGroup, tagId),
		TagStateId:           tagStateId,
		PayloadJson:          payloadJson,
		Version:              resp.Version,
		LastSortableUniqueId: lastSortable,
	}, nil
}

// CommitRequest for the serialized commit endpoint.
type CommitRequest struct {
	Events []CommitEventCandidate `json:"events"`
}

// CommitEventCandidate is a single event in a commit request.
type CommitEventCandidate struct {
	EventPayloadName string   `json:"eventPayloadName"`
	Payload          string   `json:"payload"`
	Tags             []string `json:"tags"`
}

// ConsistencyTagEntry for the commit.
type ConsistencyTagEntry struct {
	Tag                  string `json:"tag"`
	LastSortableUniqueId string `json:"lastSortableUniqueId"`
}

// CommitResponse from the serialized commit endpoint.
type CommitResponse struct {
	EventId          *string `json:"eventId"`
	SortableUniqueId *string `json:"sortableUniqueId"`
}

// ExecuteListQuery executes a list query.
func (c *SekibanRuntimeClient) ExecuteListQuery(queryType, queryParamsJson string, waitForSortableUniqueId *string) (string, error) {
	body := map[string]any{
		"queryType":      queryType,
		"queryParamsJson": queryParamsJson,
	}
	if waitForSortableUniqueId != nil {
		body["waitForSortableUniqueId"] = *waitForSortableUniqueId
	}

	var resp struct {
		ItemsJson string `json:"itemsJson"`
	}
	if err := c.postJSON("/api/sekiban/serialized/list-query", body, &resp); err != nil {
		return "[]", err
	}
	return resp.ItemsJson, nil
}

// ExecuteQuery executes a scalar query.
func (c *SekibanRuntimeClient) ExecuteQuery(queryType, queryParamsJson string, waitForSortableUniqueId *string) (string, error) {
	body := map[string]any{
		"queryType":      queryType,
		"queryParamsJson": queryParamsJson,
	}
	if waitForSortableUniqueId != nil {
		body["waitForSortableUniqueId"] = *waitForSortableUniqueId
	}

	var resp struct {
		ResultJson string `json:"resultJson"`
	}
	if err := c.postJSON("/api/sekiban/serialized/query", body, &resp); err != nil {
		return "null", err
	}
	return resp.ResultJson, nil
}

// FinalizeCommand executes a command and commits the result.
func (c *SekibanRuntimeClient) FinalizeCommand(cmd domain.Command) (*CommitResponse, error) {
	ctx := &httpCommandContext{client: c}
	output, err := cmd.Handle(ctx)
	if err != nil {
		return nil, err
	}
	if output == nil || len(output.Events) == 0 {
		return &CommitResponse{}, nil
	}

	// Build event candidates
	eventCandidates := make([]CommitEventCandidate, 0, len(output.Events))
	for _, ev := range output.Events {
		encoded := base64.StdEncoding.EncodeToString([]byte(ev.Payload))
		eventCandidates = append(eventCandidates, CommitEventCandidate{
			EventPayloadName: ev.EventType,
			Payload:          encoded,
			Tags:             output.Tags,
		})
	}

	// Build consistency tags from loaded states
	seen := make(map[string]bool)
	consistencyTags := make([]ConsistencyTagEntry, 0, len(output.ConsistencyTags))
	for _, tag := range output.ConsistencyTags {
		if seen[tag] {
			continue
		}
		seen[tag] = true
		lastSortable := ""
		if cached, ok := ctx.cache[tag]; ok && cached.LastSortableUniqueId != nil {
			lastSortable = *cached.LastSortableUniqueId
		}
		consistencyTags = append(consistencyTags, ConsistencyTagEntry{
			Tag:                  tag,
			LastSortableUniqueId: lastSortable,
		})
	}

	commitBody := map[string]any{
		"eventCandidates": eventCandidates,
		"consistencyTags": consistencyTags,
	}

	var commitResp CommitResponse
	if err := c.postJSON("/api/sekiban/serialized/commit", commitBody, &commitResp); err != nil {
		return nil, err
	}
	return &commitResp, nil
}

// httpCommandContext implements CommandContext via HTTP.
type httpCommandContext struct {
	client *SekibanRuntimeClient
	cache  map[string]domain.TagStateResponse
}

func (ctx *httpCommandContext) GetTagState(tagGroup, tagId string) (domain.TagStateResponse, error) {
	cacheKey := tagGroup + ":" + tagId
	if ctx.cache != nil {
		if cached, ok := ctx.cache[cacheKey]; ok {
			return cached, nil
		}
	}

	result, err := ctx.client.GetTagState(tagGroup, tagId)
	if err != nil {
		return domain.TagStateResponse{}, err
	}

	resp := domain.TagStateResponse{
		TagGroup:  tagGroup,
		TagId:     tagId,
		StateJson: result.PayloadJson,
		Version:   result.Version,
	}
	if result.LastSortableUniqueId != "" {
		resp.LastSortableUniqueId = &result.LastSortableUniqueId
	}

	if ctx.cache == nil {
		ctx.cache = make(map[string]domain.TagStateResponse)
	}
	ctx.cache[cacheKey] = resp
	return resp, nil
}

// HTTP helpers

func (c *SekibanRuntimeClient) postJSON(path string, payload any, target any) error {
	data, err := json.Marshal(payload)
	if err != nil {
		return err
	}
	var lastErr error
	for attempt := 0; attempt < maxRetries; attempt++ {
		resp, err := c.httpClient.Post(c.baseURL+path, "application/json", bytes.NewReader(data))
		if err != nil {
			lastErr = err
			continue
		}
		defer resp.Body.Close()
		responseBytes, _ := io.ReadAll(resp.Body)
		if resp.StatusCode >= 400 {
			return fmt.Errorf("http %d: %s", resp.StatusCode, string(responseBytes))
		}
		if target == nil {
			return nil
		}
		return json.Unmarshal(responseBytes, target)
	}
	if lastErr != nil {
		return lastErr
	}
	return errors.New("request failed after retries")
}
